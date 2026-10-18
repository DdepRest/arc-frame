using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    public static class AiCommandParser
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public static AiResponse Parse(string content, string userMessage) => TryParse(content, userMessage).Response;

        public static (AiResponse Response, bool IsValid) TryParse(string content, string userMessage)
        {
            if (string.IsNullOrWhiteSpace(content))
                return (new AiResponse { Reply = "Пустой ответ от AI." }, false);

            var json = ExtractJson(content);
            if (json != null)
            {
                try
                {
                    var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    // Plan-mode contract: { mode, reply, requires_confirmation, steps[] } or { items[] }.
                    // Steps/items may contain several actions → the whole reply is routed
                    // through the plan → preview → confirm → execute pipeline.
                    if (root.TryGetProperty("steps", out _) || root.TryGetProperty("mode", out _) || root.TryGetProperty("items", out _))
                        return TryParsePlanResponse(root, content, userMessage);

                    if (root.TryGetProperty("action", out var actionProp))
                    {
                        var (command, replyOverride) = ParseCommand(actionProp.GetString()!, root);
                        if (replyOverride != null) return (new AiResponse { Reply = replyOverride, Mode = AiPlanMode.Clarification }, true);
                        if (command == null) return (new AiResponse { Reply = content.Trim() }, false);
                        var reply = ExtractReplyText(content);
                        if (string.IsNullOrWhiteSpace(reply)) reply = GenerateActionConfirmation(command);
                        // Legacy single-action: stays as a bare Action. The
                        // safety policy is enforced one layer up — both the
                        // VM (FinalizeStreamingMessage) and the validator
                        // (Validate) run AiPlanSafetyPolicy.Classify on the
                        // candidate commands before any preview/confirm UI.
                        return (new AiResponse { Reply = reply, Action = command }, true);
                    }
                }
                catch (JsonException) { return (new AiResponse { Reply = content.Trim() }, false); }
                catch (InvalidOperationException) { return (new AiResponse { Reply = content.Trim() }, false); }
                catch (FormatException) { return (new AiResponse { Reply = content.Trim() }, false); }
            }

            // Fallback ONLY for models that leaked English chain-of-thought reasoning instead of JSON:
            if (IsChainOfThought(content))
            {
                var fromProse = TryExtractCommandFromProse(content, userMessage);
                if (fromProse.IsValid)
                    return fromProse;
            }

            return (new AiResponse { Reply = content.Trim() }, true);
        }

        private static (AiCommand? Command, string? ReplyOverride) ParseCommand(string actionType, JsonElement root)
        {
            return actionType.ToLowerInvariant() switch
            {
                "add_item" => ParseAddItem(root),
                "delete_last" => (new AiCommand { Type = AiCommandType.DeleteLast }, null),
                "clear_all" => (new AiCommand { Type = AiCommandType.ClearAll }, null),
                "list_products" => (new AiCommand { Type = AiCommandType.ListProducts }, null),
                "calc_slope" or "slope" or "open_slope" => ParseCalcSlope(root),
                "update_items" or "update_item" or "set_installation" or "set_price" => ParseUpdateItems(root),
                "delete_items" or "delete_item" => ParseDeleteItems(root),
                "update_item_by_index" or "update_row" or "edit_item" or "edit_row" => ParseUpdateItemByIndex(root),
                "delete_item_by_index" or "delete_row" => ParseDeleteItemByIndex(root),
                "toggle_item_active" or "set_active" or "toggle_active" => ParseToggleItemActive(root),
                "set_client_info" or "set_client" or "client_info" => ParseSetClientInfo(root),
                "apply_template" or "template" => ParseApplyTemplate(root),
                "save_order" => (new AiCommand { Type = AiCommandType.SaveOrder }, null),
                "print_kp" => (new AiCommand { Type = AiCommandType.PrintKp }, null),
                "export_pdf" => (new AiCommand { Type = AiCommandType.ExportPdf }, null),
                _ => (null, null)
            };
        }

        private static (AiResponse Response, bool IsValid) TryParsePlanResponse(
            JsonElement root, string content, string userMessage)
        {
            var modeStr = GetStr(root, "mode") ?? "plan";
            var mode = modeStr.ToLowerInvariant() switch
            {
                "answer" => AiPlanMode.Answer,
                "clarification" => AiPlanMode.Clarification,
                "explanation" => AiPlanMode.Explanation,
                _ => AiPlanMode.Plan
            };

            var commands = new List<AiCommand>();
            if (root.TryGetProperty("steps", out var stepsProp)
                && stepsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var stepEl in stepsProp.EnumerateArray())
                {
                    var actionType = GetStr(stepEl, "action");
                    if (string.IsNullOrWhiteSpace(actionType))
                        return (new AiResponse { Reply = content.Trim() }, false);

                    var (command, replyOverride) = ParseCommand(actionType, stepEl);
                    if (replyOverride != null)
                        return (new AiResponse { Reply = replyOverride, Mode = AiPlanMode.Clarification }, true);
                    if (command == null)
                        return (new AiResponse { Reply = content.Trim() }, false);
                    commands.Add(command);
                }
            }
            else if (root.TryGetProperty("items", out var itemsProp)
                     && itemsProp.ValueKind == JsonValueKind.Array)
            {
                var drawingItems = AiVisionConsensusService.ParseDrawingItems(content);
                int anwisMode = AiKeywordLexicon.DetectAnwisMode(userMessage) switch
                {
                    "ББ60" => 1,
                    "ББ70" => 2,
                    "Проём" => 3,
                    "Габарит" => 4,
                    _ => 0
                };
                int instMode = AiKeywordLexicon.DetectInstallationMode(userMessage);

                foreach (var di in drawingItems.Where(it => it.IsNet))
                {
                    commands.Add(new AiCommand
                    {
                        Type = AiCommandType.AddItem,
                        Params = new AiCommandParams
                        {
                            Type = di.Type,
                            Color = "Белый",
                            Width = di.Width,
                            Height = di.Height,
                            Quantity = 1,
                            AnwisMode = (AnwisSizeMode)anwisMode,
                            InstallationMode = instMode,
                            Price = GetDefaultPrice(di.Type, "Белый")
                        }
                    });
                }
            }

            // A plan without a single action = plain answer / explanation.
            bool noActions = mode is AiPlanMode.Answer or AiPlanMode.Explanation
                             || (mode == AiPlanMode.Clarification && commands.Count == 0);
            var reply = GetStr(root, "reply") ?? ExtractReplyText(content);
            if (string.IsNullOrWhiteSpace(reply) && commands.Count > 0)
                reply = "Я подготовил план действий. Подтвердите его ниже.";
            if (string.IsNullOrWhiteSpace(reply))
                reply = content.Trim();

            if (noActions)
                return (new AiResponse { Reply = reply, Mode = mode }, true);

            bool requiresConfirmation = GetBool(root, "requires_confirmation")
                || commands.Any(c => AiPlanBuilder.RequiresConfirmation(c));
            var plan = AiPlanBuilder.FromCommands(commands, userMessage, reply, mode);
            plan.RequiresConfirmation = requiresConfirmation;
            // «Don't invent»: even a plan the model shaped perfectly may still
            // need a clarification card before it can run (e.g. Anwis without
            // mode, dimensions never named). Check immediately so callers see
            // the flag as soon as they receive the (Response, bool) tuple.
            var missing = AiPlanSafetyPolicy.Classify(commands, userMessage);
            plan.NeedsClarification = missing != AiPlanSafetyPolicy.MissingField.None;
            plan.Status = plan.NeedsClarification
                ? AiPlanStatus.NeedsClarification
                : requiresConfirmation
                    ? AiPlanStatus.ReadyForPreview
                    : AiPlanStatus.Draft;
            return (new AiResponse { Reply = reply, Plan = plan, Mode = mode }, true);
        }

        private static (AiCommand? Command, string? ReplyOverride) ParseCalcSlope(JsonElement root)
        {
            var p = root.TryGetProperty("params", out var pp) ? pp : root;
            var w = GetInt(p, "width"); var h = GetInt(p, "height");
            var d = GetInt(p, "depth"); var q = GetDouble(p, "quantity", 1);
            if (w <= 0 || h <= 0 || d <= 0)
                return (null, "⚠ Для просчёта откосов укажите ширину, высоту и глубину окна в мм.");
            return (new AiCommand { Type = AiCommandType.CalcSlope, Params = new AiCommandParams { Width = w, Height = h, Depth = d, Quantity = q } }, null);
        }

        private static (AiCommand? Command, string? ReplyOverride) ParseAddItem(JsonElement root)
        {
            var p = root.TryGetProperty("params", out var pp) ? pp : root;
            var rawType = GetStr(p, "type") ?? "Anwis";
            var type = ProductCatalog.NormalizeProductName(rawType);
            var color = GetStr(p, "color");
            if (string.IsNullOrWhiteSpace(color))
                color = AiFactsProvider.GetDefaultColor(type);
            if (string.IsNullOrWhiteSpace(color))
                color = "";
            var w = GetInt(p, "width"); var h = GetInt(p, "height");
            var q = GetDouble(p, "quantity", 1); var price = GetDouble(p, "price");
            var mode = AnwisSizeService.DefaultMode; var modeStr = GetStr(p, "anwis_mode");
            if (Services.AnwisSizeService.IsApplicable(type) && string.IsNullOrEmpty(modeStr))
                return (null, "⚠ Для Anwis укажите режим: ББ60, ББ70, ПП, Проём или Габарит.");
            if (!string.IsNullOrEmpty(modeStr))
                mode = ParseAnwisModeString(modeStr);
            var im = ParseInstallationMode(p);
            bool? isAnticat = null;
            if (p.TryGetProperty("is_anticat", out var ac) || p.TryGetProperty("anticat", out ac))
            {
                if (ac.ValueKind is JsonValueKind.True or JsonValueKind.False) isAnticat = ac.GetBoolean();
                else if (ac.ValueKind == JsonValueKind.String && bool.TryParse(ac.GetString(), out var b)) isAnticat = b;
            }
            if (price <= 0) price = GetDefaultPrice(type, color);
            return (new AiCommand { Type = AiCommandType.AddItem, Params = new AiCommandParams { Type = type, Color = color, Width = w, Height = h, Quantity = q, Price = price, AnwisMode = mode, InstallationMode = im, IsAnticat = isAnticat } }, null);
        }

        private static AnwisSizeMode ParseAnwisModeString(string s) => AiKeywordLexicon.ParseAnwisModeString(s);

        private static (AiCommand? Command, string? ReplyOverride) ParseUpdateItems(JsonElement root)
        {
            var p = root.TryGetProperty("params", out var pp) ? pp : root;
            var rawTarget = GetStr(p, "product") ?? GetStr(p, "target") ?? "";
            var target = string.IsNullOrWhiteSpace(rawTarget)
                ? ""
                : (AiPlanValidator.Categories.ContainsKey(rawTarget.Trim().ToLowerInvariant())
                    ? rawTarget.Trim().ToLowerInvariant()
                    : ProductCatalog.NormalizeProductName(rawTarget));
            int? installMode = null; double? price = null; double? installAmount = null; AnwisSizeMode? anwisMode = null; string? updateColor = null;
            bool hasAny = false;

            if (p.TryGetProperty("installation_mode", out var im)) { installMode = ParseInstallationModeField(im); if (installMode.HasValue) hasAny = true; }
            if (p.TryGetProperty("price", out var pr)) { if (pr.ValueKind == JsonValueKind.Number) { price = pr.GetDouble(); if (price > 0) hasAny = true; } }
            if (p.TryGetProperty("installation_amount", out var ia) || p.TryGetProperty("install_amount", out ia)) { if (ia.ValueKind == JsonValueKind.Number) { installAmount = ia.GetDouble(); hasAny = true; } }
            if (p.TryGetProperty("anwis_mode", out var am)) { if (am.ValueKind == JsonValueKind.String) { anwisMode = ParseAnwisModeString(am.GetString()!); hasAny = true; } }
            if (p.TryGetProperty("color", out var cl)) { if (cl.ValueKind == JsonValueKind.String) { updateColor = cl.GetString(); hasAny = true; } }

            if (!hasAny) return (null, "⚠ Укажите, что нужно изменить: installation_mode (0/1/2), price, installation_amount или anwis_mode.");

            return (new AiCommand { Type = AiCommandType.UpdateItems, Params = new AiCommandParams { TargetProduct = target, UpdateInstallationMode = installMode, UpdatePrice = price, UpdateInstallationAmount = installAmount, UpdateAnwisMode = anwisMode, UpdateColor = updateColor } }, null);
        }        private static int? ParseInstallationModeField(JsonElement v) => AiKeywordLexicon.ParseInstallationModeField(v);

        private static int ParseInstallationMode(JsonElement p) => !p.TryGetProperty("installation_mode", out var v) ? -1 : ParseInstallationModeField(v) ?? -1;

        /// <summary>
        /// Catalog fallback price used when the model doesn't supply one.
        /// Public so the clarification form can price items the same way.
        /// Delegates to <see cref="AiFactsProvider.GetPrice"/> — the single
        /// source of truth that reads from <c>PriceService.DefaultPrices</c>;
        /// keeping the thin facade here preserves the public API for any
        /// caller that still asks for this name.
        /// </summary>
        public static double GetDefaultPrice(string type, string color)
            => AiFactsProvider.GetPrice(type, color);

        // ── JSON extraction ──────────────────────────────────────

        private static string? ExtractJson(string content)
        {
            int s = content.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (s >= 0)
            {
                s += 7;
                int e = content.IndexOf("```", s);
                if (e > s) return content[s..e].Trim();
                var cut = content[s..].Trim();
                if (cut.StartsWith("{"))
                {
                    int openCount = cut.Count(c => c == '{');
                    int closeCount = cut.Count(c => c == '}');
                    if (openCount > closeCount)
                    {
                        var repaired = cut + new string('}', openCount - closeCount);
                        try { using var doc = JsonDocument.Parse(repaired); return repaired; } catch { }
                    }
                }
            }
            s = content.IndexOf("```\n"); if (s < 0) s = content.IndexOf("```\r\n");
            if (s >= 0)
            {
                s += 3;
                int e = content.IndexOf("```", s);
                if (e > s) { var c = content[s..e].Trim(); if (c.StartsWith("{")) return c; }
            }
            s = content.IndexOf('{');
            if (s >= 0)
            {
                int d = 0;
                for (int i = s; i < content.Length; i++)
                {
                    if (content[i] == '{') d++;
                    else if (content[i] == '}') d--;
                    if (d == 0)
                    {
                        var c = content[s..(i + 1)];
                        if (c.Contains("\"action\"") || c.Contains("\"mode\"") || c.Contains("\"steps\"") || c.Contains("\"items\"")) return c;
                        break;
                    }
                }
                if (d > 0)
                {
                    var candidate = content[s..] + new string('}', d);
                    if (candidate.Contains("\"action\"") || candidate.Contains("\"mode\"") || candidate.Contains("\"steps\"") || candidate.Contains("\"items\""))
                    {
                        try { using var doc = JsonDocument.Parse(candidate); return candidate; } catch { }
                    }
                }
            }
            return null;
        }

        private static string ExtractReplyText(string content)
        {
            var r = content;
            while (true)
            {
                int s = r.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
                if (s < 0) break;
                int e = r.IndexOf("```", s + 7);
                if (e < 0) break;
                r = r.Remove(s, e + 3 - s);
            }
            r = r.Trim();
            if (IsChainOfThought(r))
                return string.Empty;
            return r;
        }

        private static bool IsChainOfThought(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.Contains("The user wants", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Analyze the image", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Determine the task", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Let's analyze", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Based on the sketch", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Inside the rectangle", StringComparison.OrdinalIgnoreCase);
        }

        private static (AiResponse Response, bool IsValid) TryExtractCommandFromProse(string content, string userMessage)
        {
            if (string.IsNullOrWhiteSpace(content)) return (new AiResponse { Reply = content }, false);

            // Multi-item detection: check if prose mentions multiple dimensions (e.g. 3 nets: 414x1143, 531x1162, 532x1143)
            var allDimMatches = Regex.Matches(content, @"(\d{3,5})\s*(?:mm|мм)?\s*[xх×*]\s*(\d{3,5})", RegexOptions.IgnoreCase);
            if (allDimMatches.Count > 1)
            {
                var steps = new List<AiCommand>();
                string combined = $"{userMessage} {content}";
                string anwisStr = AiKeywordLexicon.DetectAnwisMode(combined) ?? "Проём";
                var anwisMode = ParseAnwisModeString(anwisStr);
                int instMode = AiKeywordLexicon.DetectInstallationMode(combined);
                string color = AiKeywordLexicon.DetectColor(combined) ?? "Белый";

                foreach (Match m in allDimMatches)
                {
                    if (int.TryParse(m.Groups[1].Value, out var dw) && int.TryParse(m.Groups[2].Value, out var dh))
                    {
                        // Check if this pair corresponds to СП (стеклопакет) on the current line
                        int matchIdx = m.Index;
                        int lineStart = content.LastIndexOf('\n', matchIdx);
                        if (lineStart < 0) lineStart = 0;
                        int lineEnd = content.IndexOf('\n', matchIdx);
                        if (lineEnd < 0) lineEnd = content.Length;
                        string currentLine = content[lineStart..lineEnd];
                        if (currentLine.Contains("СП", StringComparison.OrdinalIgnoreCase) ||
                            currentLine.Contains("стеклопакет", StringComparison.OrdinalIgnoreCase))
                        {
                            // Glass package: skip from mosquito nets
                            continue;
                        }

                        steps.Add(new AiCommand
                        {
                            Type = AiCommandType.AddItem,
                            Params = new AiCommandParams
                            {
                                Type = "Anwis",
                                Color = color,
                                Width = dw,
                                Height = dh,
                                Quantity = 1,
                                AnwisMode = anwisMode,
                                InstallationMode = instMode,
                                Price = GetDefaultPrice("Anwis", color)
                            }
                        });
                    }
                }

                if (steps.Count > 0)
                {
                    var plan = AiPlanBuilder.FromCommands(steps, userMessage, "📋 Распознан эскиз изделий:");
                    plan.RequiresConfirmation = true;
                    plan.Status = AiPlanStatus.ReadyForPreview;
                    return (new AiResponse { Reply = plan.ReplyText, Plan = plan, Mode = AiPlanMode.Plan }, true);
                }
            }

            int width = 0;
            int height = 0;

            var wMatch = Regex.Match(content, @"(?:width|ширина)\s*[:=]\s*(\d{2,5})", RegexOptions.IgnoreCase);
            if (!wMatch.Success)
                wMatch = Regex.Match(content, @"(\d{2,5})\s*(?:mm)?\s*\(width\)", RegexOptions.IgnoreCase);

            var hMatch = Regex.Match(content, @"(?:height|высота)\s*[:=]\s*(\d{2,5})", RegexOptions.IgnoreCase);
            if (!hMatch.Success)
                hMatch = Regex.Match(content, @"(\d{2,5})\s*(?:mm)?\s*\(height\)", RegexOptions.IgnoreCase);

            if (wMatch.Success && int.TryParse(wMatch.Groups[1].Value, out var w)) width = w;
            if (hMatch.Success && int.TryParse(hMatch.Groups[1].Value, out var h)) height = h;

            if (width <= 0 || height <= 0)
            {
                var dimMatch = Regex.Match(content, @"(\d{2,5})\s*(?:mm)?\s*[xх×*]\s*(\d{2,5})", RegexOptions.IgnoreCase);
                if (dimMatch.Success)
                {
                    if (int.TryParse(dimMatch.Groups[1].Value, out var dw)) width = dw;
                    if (int.TryParse(dimMatch.Groups[2].Value, out var dh)) height = dh;
                }
            }

            if (width > 0 && height > 0)
            {
                string combined = $"{userMessage} {content}";
                string product = "Anwis";
                if (combined.Contains("отлив", StringComparison.OrdinalIgnoreCase)) product = "Отлив";
                else if (combined.Contains("козырёк", StringComparison.OrdinalIgnoreCase)) product = "Козырёк";
                else if (combined.Contains("дверн", StringComparison.OrdinalIgnoreCase)) product = "Дверная сетка";

                string anwisStr = AiKeywordLexicon.DetectAnwisMode(combined) ?? "Проём";
                var anwisMode = ParseAnwisModeString(anwisStr);
                int instMode = AiKeywordLexicon.DetectInstallationMode(combined);

                var cmd = new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams
                    {
                        Type = product,
                        Color = AiKeywordLexicon.DetectColor(combined) ?? "Белый",
                        Width = width,
                        Height = height,
                        Quantity = 1,
                        AnwisMode = anwisMode,
                        InstallationMode = instMode,
                        Price = GetDefaultPrice(product, AiKeywordLexicon.DetectColor(combined) ?? "Белый")
                    }
                };
                string reply = GenerateActionConfirmation(cmd);
                return (new AiResponse { Reply = reply, Action = cmd }, true);
            }

            return (new AiResponse { Reply = content.Trim() }, false);
        }

        // ── Confirmation builders ────────────────────────────────

        /// <summary>
        /// Builds the human-readable confirmation for a parsed command.
        /// Public so the clarification form can reuse the exact same wording.
        /// </summary>
        public static string GenerateActionConfirmation(AiCommand command) => command.Type switch
        {
            AiCommandType.AddItem => BuildAddItemConfirmation(command),
            AiCommandType.DeleteLast => "✅ Последняя позиция удалена.",
            AiCommandType.ClearAll => "✅ Расчёт очищен.",
            AiCommandType.ListProducts => "📋 Список товаров отправлен.",
            AiCommandType.CalcSlope => $"✅ Открыт просчёт откосов: {command.Params.Width}×{command.Params.Height} мм, глубина {command.Params.Depth} мм, {command.Params.Quantity} отк.",
            AiCommandType.UpdateItems => BuildUpdateItemsConfirmation(command),
            AiCommandType.DeleteItems => BuildDeleteItemsConfirmation(command),
            AiCommandType.UpdateItemByIndex => BuildUpdateItemByIndexConfirmation(command),
            AiCommandType.DeleteItemByIndex => $"🗑 Удалена строка #{command.Params.ItemIndex}.",
            AiCommandType.ToggleItemActive => $"✅ Строка #{command.Params.ItemIndex} {(command.Params.IsActive == false ? "отключена" : "включена")}.",
            AiCommandType.SetClientInfo => "📋 Данные клиента обновлены.",
            AiCommandType.ApplyTemplate => $"📐 Применён шаблон «{command.Params.TemplateId}».",
            AiCommandType.SaveOrder => "💾 Заказ сохранён.",
            AiCommandType.PrintKp => "🖨 Коммерческое предложение отправлено на печать.",
            AiCommandType.ExportPdf => "📄 Коммерческое предложение экспортировано в PDF.",
            _ => "✅ Готово."
        };

        private static string BuildDeleteItemsConfirmation(AiCommand c) { var t = string.IsNullOrWhiteSpace(c.Params.TargetProduct) ? "все позиции" : $"«{c.Params.TargetProduct}»"; return $"🗑 Удалены позиции: {t}"; }
        private static string BuildAddItemConfirmation(AiCommand c)
        {
            var p = c.Params;
            var anticat = p.IsAnticat == true ? " [Антикошка]" : "";
            var m = $"✅ Добавлено: {p.Type}{anticat} {p.Color} {p.Width}×{p.Height} мм, {p.Quantity} шт. по {p.Price:N0} ₽";
            return p.InstallationMode switch
            {
                0 => $"{m}, монтаж включён",
                1 => $"{m}, без монтажа",
                2 => $"{m}, в конструкцию",
                _ => m
            };
        }

        private static string BuildUpdateItemsConfirmation(AiCommand c)
        {
            var p = c.Params; var t = string.IsNullOrWhiteSpace(p.TargetProduct) ? "все позиции" : $"«{p.TargetProduct}»"; var parts = new List<string>();
            if (p.UpdateInstallationMode.HasValue) parts.Add($"монтаж → {p.UpdateInstallationMode.Value switch { 0 => "монтаж включён", 1 => "без монтажа", 2 => "в конструкцию", _ => "изменён" }}");
            if (p.UpdateAnwisMode.HasValue) parts.Add($"Anwis-режим → {AnwisModeLabel(p.UpdateAnwisMode.Value)}");
            if (p.UpdateColor != null) parts.Add($"цвет → {p.UpdateColor}");
            if (p.UpdateInstallationAmount.HasValue) parts.Add($"сумма монтажа → {p.UpdateInstallationAmount.Value:N0} ₽");
            if (p.UpdatePrice.HasValue) parts.Add($"цена → {p.UpdatePrice.Value:N0} ₽");
            return $"✅ Обновлено ({t}): {string.Join(", ", parts)}";
        }

        private static string BuildUpdateItemByIndexConfirmation(AiCommand c)
        {
            var p = c.Params;
            var parts = new List<string>();
            if (p.UpdateWidth.HasValue || p.UpdateHeight.HasValue)
                parts.Add($"размеры → {(p.UpdateWidth?.ToString() ?? "—")}×{(p.UpdateHeight?.ToString() ?? "—")} мм");
            if (p.UpdateQuantity.HasValue) parts.Add($"кол-во → {p.UpdateQuantity.Value} шт.");
            if (p.UpdatePrice.HasValue) parts.Add($"цена → {p.UpdatePrice.Value:N0} ₽");
            if (p.UpdateColor != null) parts.Add($"цвет → {p.UpdateColor}");
            if (p.UpdateInstallationMode.HasValue) parts.Add($"монтаж → {p.UpdateInstallationMode.Value switch { 0 => "включён", 1 => "без монтажа", 2 => "в конструкцию", _ => "изменён" }}");
            if (p.UpdateAnwisMode.HasValue) parts.Add($"Anwis-режим → {AnwisModeLabel(p.UpdateAnwisMode.Value)}");
            if (p.IsAnticat.HasValue) parts.Add($"антикошка → {(p.IsAnticat.Value ? "да" : "нет")}");
            if (p.IsActive.HasValue) parts.Add($"активность → {(p.IsActive.Value ? "включена" : "отключена")}");
            return $"✅ Строка #{p.ItemIndex} обновлена: {string.Join(", ", parts)}";
        }

        internal static string AnwisModeLabel(AnwisSizeMode m) => AiKeywordLexicon.AnwisModeLabel(m);

        // ── Helpers ───────────────────────────────────────────────

        private static string? GetStr(JsonElement el, string name) => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static int GetInt(JsonElement el, string name, int def = 0) => el.TryGetProperty(name, out var v) ? v.GetInt32() : def;
        private static double GetDouble(JsonElement el, string name, double def = 0) => el.TryGetProperty(name, out var v) ? v.GetDouble() : def;
        private static bool GetBool(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v)) return false;
            if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
            return v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b;
        }

        private static (AiCommand? Command, string? ReplyOverride) ParseDeleteItems(JsonElement root)
        {
            var p = root.TryGetProperty("params", out var pp) ? pp : root;
            var target = GetStr(p, "product") ?? GetStr(p, "target") ?? "";
            return (new AiCommand { Type = AiCommandType.DeleteItems, Params = new AiCommandParams { TargetProduct = target } }, null);
        }

        private static (AiCommand? Command, string? ReplyOverride) ParseUpdateItemByIndex(JsonElement root)
        {
            var p = root.TryGetProperty("params", out var pp) ? pp : root;
            int idx = GetInt(p, "index", 0);
            if (idx <= 0) idx = GetInt(p, "item_index", 0);
            if (idx <= 0) idx = GetInt(p, "row", 0);
            if (idx <= 0)
                return (null, "⚠ Укажите номер строки (index > 0), которую требуется изменить.");

            int? w = p.TryGetProperty("width", out var wp) && wp.ValueKind == JsonValueKind.Number ? wp.GetInt32() : null;
            int? h = p.TryGetProperty("height", out var hp) && hp.ValueKind == JsonValueKind.Number ? hp.GetInt32() : null;
            double? q = p.TryGetProperty("quantity", out var qp) && qp.ValueKind == JsonValueKind.Number ? qp.GetDouble() : null;
            double? price = p.TryGetProperty("price", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetDouble() : null;
            int? im = p.TryGetProperty("installation_mode", out var imp) ? ParseInstallationModeField(imp) : null;
            string? color = p.TryGetProperty("color", out var cl) && cl.ValueKind == JsonValueKind.String ? cl.GetString() : null;
            AnwisSizeMode? am = p.TryGetProperty("anwis_mode", out var amp) && amp.ValueKind == JsonValueKind.String ? ParseAnwisModeString(amp.GetString()!) : null;

            bool? isAnticat = null;
            if (p.TryGetProperty("is_anticat", out var ac) || p.TryGetProperty("anticat", out ac))
            {
                if (ac.ValueKind is JsonValueKind.True or JsonValueKind.False) isAnticat = ac.GetBoolean();
                else if (ac.ValueKind == JsonValueKind.String && bool.TryParse(ac.GetString(), out var b)) isAnticat = b;
            }

            bool? isActive = null;
            if (p.TryGetProperty("is_active", out var ia) || p.TryGetProperty("active", out ia))
            {
                if (ia.ValueKind is JsonValueKind.True or JsonValueKind.False) isActive = ia.GetBoolean();
                else if (ia.ValueKind == JsonValueKind.String && bool.TryParse(ia.GetString(), out var b)) isActive = b;
            }

            return (new AiCommand
            {
                Type = AiCommandType.UpdateItemByIndex,
                Params = new AiCommandParams
                {
                    ItemIndex = idx,
                    UpdateWidth = w,
                    UpdateHeight = h,
                    UpdateQuantity = q,
                    UpdatePrice = price,
                    UpdateInstallationMode = im,
                    UpdateColor = color,
                    UpdateAnwisMode = am,
                    IsAnticat = isAnticat,
                    IsActive = isActive
                }
            }, null);
        }

        private static (AiCommand? Command, string? ReplyOverride) ParseDeleteItemByIndex(JsonElement root)
        {
            var p = root.TryGetProperty("params", out var pp) ? pp : root;
            int idx = GetInt(p, "index", 0);
            if (idx <= 0) idx = GetInt(p, "item_index", 0);
            if (idx <= 0) idx = GetInt(p, "row", 0);
            if (idx <= 0)
                return (null, "⚠ Укажите корректный номер строки для удаления (например, index: 1).");

            return (new AiCommand
            {
                Type = AiCommandType.DeleteItemByIndex,
                Params = new AiCommandParams { ItemIndex = idx }
            }, null);
        }

        private static (AiCommand? Command, string? ReplyOverride) ParseToggleItemActive(JsonElement root)
        {
            var p = root.TryGetProperty("params", out var pp) ? pp : root;
            int idx = GetInt(p, "index", 0);
            if (idx <= 0) idx = GetInt(p, "item_index", 0);
            if (idx <= 0) idx = GetInt(p, "row", 0);
            if (idx <= 0)
                return (null, "⚠ Укажите корректный номер строки (например, index: 1).");

            bool? isActive = null;
            if (p.TryGetProperty("is_active", out var ia) || p.TryGetProperty("active", out ia))
            {
                if (ia.ValueKind is JsonValueKind.True or JsonValueKind.False) isActive = ia.GetBoolean();
                else if (ia.ValueKind == JsonValueKind.String && bool.TryParse(ia.GetString(), out var b)) isActive = b;
            }

            return (new AiCommand
            {
                Type = AiCommandType.ToggleItemActive,
                Params = new AiCommandParams { ItemIndex = idx, IsActive = isActive }
            }, null);
        }

        private static (AiCommand? Command, string? ReplyOverride) ParseSetClientInfo(JsonElement root)
        {
            var p = root.TryGetProperty("params", out var pp) ? pp : root;
            var phone = GetStr(p, "phone") ?? GetStr(p, "client_phone") ?? "";
            var addr = GetStr(p, "address") ?? GetStr(p, "client_address") ?? "";
            var contract = GetStr(p, "contract") ?? GetStr(p, "contract_number") ?? "";
            var notes = GetStr(p, "notes") ?? GetStr(p, "comment") ?? "";

            return (new AiCommand
            {
                Type = AiCommandType.SetClientInfo,
                Params = new AiCommandParams
                {
                    ClientPhone = phone,
                    ClientAddress = addr,
                    ContractNumber = contract,
                    Notes = notes
                }
            }, null);
        }

        private static (AiCommand? Command, string? ReplyOverride) ParseApplyTemplate(JsonElement root)
        {
            var p = root.TryGetProperty("params", out var pp) ? pp : root;
            var templateId = GetStr(p, "template_id") ?? GetStr(p, "template") ?? "";
            var w = GetInt(p, "width");
            var h = GetInt(p, "height");
            var color = GetStr(p, "color") ?? "";

            if (string.IsNullOrWhiteSpace(templateId))
                return (null, "⚠ Укажите идентификатор шаблона (window, balcony, frame, french).");

            return (new AiCommand
            {
                Type = AiCommandType.ApplyTemplate,
                Params = new AiCommandParams
                {
                    TemplateId = templateId.ToLowerInvariant().Trim(),
                    Width = w,
                    Height = h,
                    Color = color
                }
            }, null);
        }
    }
}
