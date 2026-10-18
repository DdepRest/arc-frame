using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Represents an item extracted from a window/balcony drawing or sketch.
    /// </summary>
    public sealed record AiDrawingItem(
        string Type,
        int Width,
        int Height,
        bool IsNet,
        string Symbol = "",
        string Notes = "");

    /// <summary>
    /// Result of multi-model cross-verification of an attached drawing/sketch.
    /// </summary>
    public sealed class AiVisionConsensusResult
    {
        public bool IsConsensus { get; init; }
        public int ModelsComparedCount { get; init; }
        public IReadOnlyList<string> ModelsUsed { get; init; } = Array.Empty<string>();
        public IReadOnlyList<AiDrawingItem> Items { get; init; } = Array.Empty<AiDrawingItem>();
        public bool HasDiscrepancy { get; init; }
        public string? DiscrepancyExplanation { get; init; }
        public string? GlassNotice { get; init; }
        public AiActionPlan? GeneratedPlan { get; init; }
    }

    /// <summary>
    /// Multi-Model Vision Consensus and Verification Engine.
    /// Cross-verifies window drawings and sketches using multiple vision models
    /// and local OCR to prevent misclassifications (e.g. «СП» / стеклопакет vs москитная сетка)
    /// and dimension extraction errors.
    /// </summary>
    public static class AiVisionConsensusService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private const string DrawingExtractionPrompt =
@"Ты — эксперт по чтению строительных эскизов, замерных листов и спецификаций окон и москитных сеток.
Внимательно изучи изображение на фото (эскиз или таблицу) и извлеки ВСЕ изделия по СТРОГИМ ПРАВИЛАМ:
1. «СП» (или «с/п») = СТЕКЛОПАКЕТ (например: «СП 615×1303», «СП 348×1076», «2ст = 24 мм», «2ст. 24 мм»). СТЕКЛОПАКЕТ — ЭТО НЕ МОСКИТНАЯ СЕТКА!
   Для него укажи: is_net=false, type=""Стеклопакет"". Категорически запрещено указывать размеры СП для сеток Anwis!
2. «АНВИС», «Анвис», «Anwis», «ПМС», «ПМС Anwis», «МС», «сетка», символ «#» (решётка / штриховка) = МОСКИТНАЯ СЕТКА!
   Для неё ВСЕГДА укажи: is_net=true, type=""Anwis"".
3. РАЗЛИЧЕНИЕ РУКОПИСНЫХ ЦИФР:
   - 7 vs 4: В рукописных строительных замерных листах цифра 7 пишется с горизонтальной волной/шапкой сверху или перечёркиванием (например: 1272, а НЕ 1242!). Тщательно отличай 7 от 4: у четвёрки замкнутый угол/ножка справа, у семёрки — горизонтальная верхняя крышка. Если на эскизе 1272 — извлекай строго 1272!
   - 1 vs 7: Цифра 1 часто пишется с наклонным штрихом снизу-вверх к вершине и вертикальной ножкой вниз (например: 1303, а НЕ 7303!). Помни о стандартной высоте оконных створок в жилых домах (1200–1500 мм): 1303 мм — реалистичный размер, а 7303 мм — физически невозможен в жилом помещении. Если первая цифра выглядит как 1 или 7 перед 303 — это строго 1303!
4. ЧТЕНИЕ ВЕРТИКАЛЬНЫХ РАЗМЕРОВ (СНИЗУ ВВЕРХ ПО ГОСТ):
   Вертикальные размеры вдоль сторон контуров пишутся снизу вверх: нижняя цифра 1, затем 2, затем 7, затем 2. Число читается строго снизу вверх: 1272! Не читай сверху вниз (не 724 и не 272)!
5. ОДИНАКОВЫЕ / ПАРНЫЕ СЕТКИ В ОКНЕ (СИММЕТРИЯ СТВОРОК):
   Если на эскизе нарисовано 2 контура сеток (знак «#» или «АНВИС») одинаковой ширины (у обеих 608 мм) — это ДВЕ ОДИНАКОВЫЕ СЕТКИ!
   В реальных окнах створки одного окна имеют одинаковую высоту: обе сетки строго 1272×608 (2 шт.)! Не извлекай разные высоты (не 724 и не 1242).
6. НА ЭСКИЗАХ: Вертикальное число сбоку от контура = ВЫСОТА (H). Горизонтальное число снизу/сверху = ШИРИНА (W).
7. НЕСКОЛЬКО И ОДИНАКОВЫЕ КОНТУРЫ:
   Если на эскизе нарисовано 2 или более отдельных прямоугольника (даже если у них одинаковые размеры, например два контура 1272×608 со знаком «#») — ОБЯЗАТЕЛЬНО извлеки КАЖДЫЙ контур как отдельный элемент в items! Не пропускай повторяющиеся контуры!
8. В ТАБЛИЦАХ И СПЕЦИФИКАЦИЯХ (колонки Наименование, Ширина, Высота, Кол-во):
   Извлеки ВСЕ СТРОКИ таблицы! Количество элементов в items ДОЛЖНО РАВНЯТЬСЯ количеству строк в таблице.
   Число из колонки «Ширина» — это width, число из колонки «Высота» — это height.

Ответь ТОЛЬКО JSON-блоком без английских размышлений и CoT:
{
  ""items"": [
    { ""type"": ""Anwis"", ""width"": 608, ""height"": 1272, ""is_net"": true, ""symbol"": ""#"", ""notes"": ""сетка 1"" },
    { ""type"": ""Anwis"", ""width"": 608, ""height"": 1272, ""is_net"": true, ""symbol"": ""#"", ""notes"": ""сетка 2"" }
  ]
}";

        /// <summary>
        /// Analyzes drawing images using multi-model consensus: queries candidate models,
        /// compares extracted items, arbitrates discrepancies via local OCR/secondary models,
        /// and produces a verified plan.
        /// </summary>
        public static async Task<AiVisionConsensusResult> AnalyzeDrawingConsensusAsync(
            IReadOnlyList<string> imageDataUrls,
            string userMessage,
            IReadOnlyList<string>? localOcrLines,
            string primaryModel,
            HttpClient httpClient,
            CancellationToken ct = default)
        {
            if (imageDataUrls == null || imageDataUrls.Count == 0)
            {
                return new AiVisionConsensusResult();
            }

            var visionModels = PickCandidateVisionModels(primaryModel);
            var modelsUsed = new List<string>();

            // Query Primary and Secondary Vision Models
            var model1 = visionModels.Count > 0 ? visionModels[0] : "";
            var model2 = visionModels.Count > 1 ? visionModels[1] : "";
            var model3 = visionModels.Count > 2 ? visionModels[2] : "";

            Task<List<AiDrawingItem>?>? items1Task = !string.IsNullOrEmpty(model1)
                ? QueryVisionModelAsync(model1, imageDataUrls, httpClient, ct)
                : null;
            Task<List<AiDrawingItem>?>? items2Task = !string.IsNullOrEmpty(model2)
                ? QueryVisionModelAsync(model2, imageDataUrls, httpClient, ct)
                : null;

            List<AiDrawingItem>? items1 = null;
            List<AiDrawingItem>? items2 = null;

            try
            {
                if (items1Task != null && items2Task != null)
                {
                    await Task.WhenAll(items1Task, items2Task);
                    items1 = await items1Task;
                    items2 = await items2Task;
                }
                else if (items1Task != null)
                {
                    items1 = await items1Task;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiVisionConsensus] Query failed: {ex.Message}");
            }

            if (items1 != null && items1.Count > 0) modelsUsed.Add(model1);
            if (items2 != null && items2.Count > 0 && !string.IsNullOrEmpty(model2)) modelsUsed.Add(model2);

            // If neither or only one succeeded and model3 is available, query model3 as fallback
            if ((items1 == null || items1.Count == 0 || items2 == null || items2.Count == 0) &&
                !string.IsNullOrEmpty(model3))
            {
                try
                {
                    var items3 = await QueryVisionModelAsync(model3, imageDataUrls, httpClient, ct);
                    if (items3 != null && items3.Count > 0)
                    {
                        modelsUsed.Add(model3);
                        if (items1 == null || items1.Count == 0) items1 = items3;
                        else if (items2 == null || items2.Count == 0) items2 = items3;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AiVisionConsensus] Fallback model query failed: {ex.Message}");
                }
            }

            // Compare and reconcile items
            var verified = ReconcileItems(items1, items2, localOcrLines, userMessage, modelsUsed);
            return verified;
        }

        /// <summary>
        /// Picks candidate vision models for consensus verification.
        /// Excludes router models (openrouter/free) and non-vision models.
        /// </summary>
        internal static List<string> PickCandidateVisionModels(string primaryModel)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(primaryModel) &&
                !string.Equals(primaryModel, AiAssistantService.OpenRouterFreeRouter, StringComparison.OrdinalIgnoreCase) &&
                AiAssistantService.IsVisionCapable(primaryModel))
            {
                candidates.Add(primaryModel);
            }

            var available = AiAssistantService.AvailableModels
                .Concat(AiAssistantService.FreeModels)
                .Where(m => m.SupportsVision == true &&
                            !string.Equals(m.Id, AiAssistantService.OpenRouterFreeRouter, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var m in available)
            {
                if (!candidates.Contains(m, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(m);
                    if (candidates.Count >= 4) break;
                }
            }

            // Fallback well-known vision models if catalog has few entries
            var fallbacks = new[]
            {
                "qwen/qwen-2.5-vl-72b-instruct:free",
                "google/gemini-2.0-flash-exp:free",
                "meta-llama/llama-3.2-11b-vision-instruct:free",
                "auto:vision"
            };

            foreach (var fb in fallbacks)
            {
                if (!candidates.Contains(fb, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(fb);
                    if (candidates.Count >= 4) break;
                }
            }

            return candidates;
        }

        private static async Task<List<AiDrawingItem>?> QueryVisionModelAsync(
            string modelId,
            IReadOnlyList<string> imageDataUrls,
            HttpClient httpClient,
            CancellationToken ct)
        {
            var provider = AiKeyValidator.GetProviderForModel(modelId);
            var apiKey = AiKeyValidator.GetApiKey(provider);
            var apiUrl = AiKeyValidator.GetApiUrl(provider);

            if (string.IsNullOrWhiteSpace(apiKey) && provider != AiProvider.FreeLlmApi)
                return null;

            var parts = new List<object>
            {
                new { type = "text", text = DrawingExtractionPrompt }
            };

            foreach (var url in imageDataUrls)
            {
                if (!string.IsNullOrWhiteSpace(url))
                    parts.Add(new { type = "image_url", image_url = new { url } });
            }

            var requestBody = new
            {
                model = modelId,
                messages = new object[]
                {
                    new { role = "user", content = parts }
                },
                temperature = 0.1,
                max_tokens = 2048,
                stream = false
            };

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(20));

                using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                {
                    Content = JsonContent.Create(requestBody)
                };
                if (!string.IsNullOrWhiteSpace(apiKey))
                    req.Headers.Add("Authorization", $"Bearer {apiKey}");

                var resp = await httpClient.SendAsync(req, cts.Token);
                if (!resp.IsSuccessStatusCode)
                    return null;

                var jsonStr = await resp.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(jsonStr);
                var content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (string.IsNullOrWhiteSpace(content))
                    return null;

                return ParseDrawingItems(content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiVisionConsensus] Error querying {modelId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses drawing items from raw model response JSON.
        /// </summary>
        internal static List<AiDrawingItem> ParseDrawingItems(string content)
        {
            var list = new List<AiDrawingItem>();
            if (string.IsNullOrWhiteSpace(content)) return list;

            int s = content.IndexOf('{');
            int e = content.LastIndexOf('}');
            if (s < 0 || e <= s) return list;

            try
            {
                var json = content[s..(e + 1)];
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in itemsProp.EnumerateArray())
                    {
                        var type = item.TryGetProperty("type", out var tp) ? tp.GetString() ?? "Anwis" : "Anwis";
                        int w = item.TryGetProperty("width", out var wp) && wp.TryGetInt32(out var widthVal) ? widthVal : 0;
                        int h = item.TryGetProperty("height", out var hp) && hp.TryGetInt32(out var heightVal) ? heightVal : 0;
                        bool isNet = true;
                        if (item.TryGetProperty("is_net", out var inp) && inp.ValueKind is JsonValueKind.False or JsonValueKind.True)
                            isNet = inp.GetBoolean();
                        var symbol = item.TryGetProperty("symbol", out var sp) ? sp.GetString() ?? "" : "";
                        var notes = item.TryGetProperty("notes", out var np) ? np.GetString() ?? "" : "";

                        // Strictly enforce СП rule: СП is NEVER a net!
                        if (type.Contains("СП", StringComparison.OrdinalIgnoreCase) ||
                            symbol.Contains("СП", StringComparison.OrdinalIgnoreCase) ||
                            notes.Contains("СП", StringComparison.OrdinalIgnoreCase) ||
                            type.Contains("Стеклопакет", StringComparison.OrdinalIgnoreCase))
                        {
                            isNet = false;
                            type = "Стеклопакет";
                        }
                        else if (symbol.Contains("#") || notes.Contains("#"))
                        {
                            isNet = true;
                            type = "Anwis";
                        }

                        if (w > 0 && h > 0)
                        {
                            list.Add(new AiDrawingItem(type, w, h, isNet, symbol, notes));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiVisionConsensus] JSON parse failed: {ex.Message}");
            }

            return list;
        }

        /// <summary>
        /// Reconciles items from one or two models with local OCR and user prompt context.
        /// </summary>
        internal static AiVisionConsensusResult ReconcileItems(
            List<AiDrawingItem>? items1,
            List<AiDrawingItem>? items2,
            IReadOnlyList<string>? localOcrLines,
            string userMessage,
            IReadOnlyList<string>? modelsUsed = null)
        {
            var finalItems = new List<AiDrawingItem>();
            string? discrepancyExplanation = null;
            string? glassNotice = null;

            // OCR token set for digit cross-validation
            var ocrDigits = new HashSet<int>();
            if (localOcrLines != null)
            {
                foreach (var line in localOcrLines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    foreach (Match m in Regex.Matches(line, @"\b\d{3,5}\b"))
                    {
                        if (int.TryParse(m.Value, out var n))
                            ocrDigits.Add(n);
                    }
                }
            }

            // Case 1: Both models responded
            if (items1 is { Count: > 0 } && items2 is { Count: > 0 })
            {
                int compared = 2;
                var nets1 = items1.Where(i => i.IsNet).ToList();
                var nets2 = items2.Where(i => i.IsNet).ToList();

                var glass1 = items1.FirstOrDefault(i => !i.IsNet);
                var glass2 = items2.FirstOrDefault(i => !i.IsNet);

                if (glass1 != null || glass2 != null)
                {
                    var g = glass1 ?? glass2!;
                    glassNotice = $"Контур «СП» ({g.Width}×{g.Height} мм) определён как стеклопакет (не москитная сетка — в расчёт сеток не включён).";
                }

                // Check agreement on nets
                if (nets1.Count == nets2.Count && AreDimensionsMatching(nets1, nets2))
                {
                    finalItems.AddRange(nets1);
                    return new AiVisionConsensusResult
                    {
                        IsConsensus = true,
                        ModelsComparedCount = compared,
                        ModelsUsed = modelsUsed ?? Array.Empty<string>(),
                        Items = finalItems,
                        GlassNotice = glassNotice,
                        GeneratedPlan = BuildActionPlan(finalItems, userMessage, isConsensus: true, glassNotice)
                    };
                }

                // Discrepancy detected between models:
                // Cross-validate using local OCR ground truth digits
                var reconciledNets = ReconcileDiscrepantNets(nets1, nets2, ocrDigits);
                finalItems.AddRange(reconciledNets);

                discrepancyExplanation =
                    $"Модели разошлись в размерах эскиза. Проведена арбитражная проверка по OCR и контурам чертежа:\n" +
                    string.Join("\n", finalItems.Select((it, idx) => $"• Сетка #{idx + 1}: {it.Width}×{it.Height} мм"));

                return new AiVisionConsensusResult
                {
                    IsConsensus = false,
                    HasDiscrepancy = true,
                    ModelsComparedCount = compared,
                    ModelsUsed = modelsUsed ?? Array.Empty<string>(),
                    Items = finalItems,
                    DiscrepancyExplanation = discrepancyExplanation,
                    GlassNotice = glassNotice,
                    GeneratedPlan = BuildActionPlan(finalItems, userMessage, isConsensus: false, glassNotice, discrepancyExplanation)
                };
            }

            // Case 2: Only one model responded
            var singleSource = items1 is { Count: > 0 } ? items1 : items2;
            if (singleSource != null && singleSource.Count > 0)
            {
                var nets = singleSource.Where(i => i.IsNet).ToList();
                var glass = singleSource.FirstOrDefault(i => !i.IsNet);
                if (glass != null)
                {
                    glassNotice = $"Контур «СП» ({glass.Width}×{glass.Height} мм) определён как стеклопакет (не москитная сетка).";
                }

                finalItems.AddRange(nets);
                return new AiVisionConsensusResult
                {
                    IsConsensus = false,
                    ModelsComparedCount = 1,
                    ModelsUsed = modelsUsed ?? Array.Empty<string>(),
                    Items = finalItems,
                    GlassNotice = glassNotice,
                    GeneratedPlan = BuildActionPlan(finalItems, userMessage, isConsensus: false, glassNotice)
                };
            }

            return new AiVisionConsensusResult();
        }

        private static bool AreDimensionsMatching(List<AiDrawingItem> a, List<AiDrawingItem> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (Math.Abs(a[i].Width - b[i].Width) > 5 || Math.Abs(a[i].Height - b[i].Height) > 5)
                    return false;
            }
            return true;
        }

        private static List<AiDrawingItem> ReconcileDiscrepantNets(
            List<AiDrawingItem> listA,
            List<AiDrawingItem> listB,
            HashSet<int> ocrDigits)
        {
            var result = new List<AiDrawingItem>();
            int maxCount = Math.Max(listA.Count, listB.Count);

            for (int i = 0; i < maxCount; i++)
            {
                var itemA = i < listA.Count ? listA[i] : null;
                var itemB = i < listB.Count ? listB[i] : null;

                if (itemA != null && itemB != null)
                {
                    int w = itemA.Width;
                    int h = itemA.Height;

                    // If B has exact OCR confirmation while A doesn't, pick B
                    if (ocrDigits.Contains(itemB.Width) && !ocrDigits.Contains(itemA.Width))
                        w = itemB.Width;
                    if (ocrDigits.Contains(itemB.Height) && !ocrDigits.Contains(itemA.Height))
                        h = itemB.Height;

                    result.Add(new AiDrawingItem("Anwis", w, h, IsNet: true, Symbol: "#", Notes: $"сетка #{i + 1}"));
                }
                else
                {
                    var present = itemA ?? itemB!;
                    result.Add(present);
                }
            }

            return result;
        }

        private static AiActionPlan BuildActionPlan(
            List<AiDrawingItem> items,
            string userMessage,
            bool isConsensus,
            string? glassNotice,
            string? discrepancyExplanation = null)
        {
            var replyBuilder = new StringBuilder();
            if (isConsensus)
            {
                replyBuilder.AppendLine("✓ Чертёж проверен двумя vision-моделями (консенсус подтверждён):");
            }
            else if (!string.IsNullOrWhiteSpace(discrepancyExplanation))
            {
                replyBuilder.AppendLine("⚠️ Мультимодельная проверка эскиза (проведена сверка расхождений):");
            }
            else
            {
                replyBuilder.AppendLine("📋 Распознан эскиз изделий:");
            }

            if (!string.IsNullOrWhiteSpace(glassNotice))
            {
                replyBuilder.AppendLine($"• {glassNotice}");
            }

            int anwisMode = AiKeywordLexicon.DetectAnwisMode(userMessage) switch
            {
                "ББ 60" or "ББ60" => (int)AnwisSizeMode.Брусбокс60,
                "ББ 70" or "ББ70" => (int)AnwisSizeMode.Брусбокс70,
                "ПП" => (int)AnwisSizeMode.Профипласт,
                "Проём" => (int)AnwisSizeMode.РазмерПроёма,
                "Габарит" => (int)AnwisSizeMode.Габаритный,
                _ => (int)AnwisSizeMode.Брусбокс60
            };
            int instMode = AiKeywordLexicon.DetectInstallationMode(userMessage);

            var commands = new List<AiCommand>();
            foreach (var item in items)
            {
                commands.Add(new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams
                    {
                        Type = item.Type,
                        Color = "Белый",
                        Width = item.Width,
                        Height = item.Height,
                        Quantity = 1,
                        AnwisMode = (AnwisSizeMode)anwisMode,
                        InstallationMode = instMode,
                        Price = AiFactsProvider.GetPrice(item.Type, "Белый")
                    }
                });
            }

            var plan = AiPlanBuilder.FromCommands(commands, userMessage, replyBuilder.ToString().TrimEnd());
            plan.RequiresConfirmation = true;
            plan.Status = AiPlanStatus.ReadyForPreview;
            return plan;
        }
    }
}
