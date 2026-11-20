using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.ViewModels;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Result of PVC window structure recognition and CCalc calculation.
    /// </summary>
    public sealed class PvcSketchResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string Address { get; set; } = "";
        public string OrderNumber { get; set; } = "";
        public double TotalAmount { get; set; }
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public int NetWidth { get; set; }
        public int NetHeight { get; set; }
        public int DrainageWidth { get; set; }
        public int DrainageLength { get; set; }
        public int SillWidth { get; set; }
        public int SillLength { get; set; }
    }

    /// <summary>
    /// Bridge service connecting A.R.C. Frame with FreeLLMAPI Vision and CCalc 9.12 RPA Bot.
    /// </summary>
    public static class PvcAutoCalcService
    {
        private static string GetScriptPath()
        {
            // First check relative to app base directory, then project root tools directory
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var path1 = Path.Combine(baseDir, "tools", "pvc_bridge", "process_sketch.py");
            if (File.Exists(path1)) return path1;

            var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            var path2 = Path.Combine(projectRoot, "tools", "pvc_bridge", "process_sketch.py");
            if (File.Exists(path2)) return path2;

            return path1;
        }

        /// <summary>
        /// Каталог реестра заказов, который бот пишет после сохранения в CCalc
        /// (tools/pvc_bridge/orders/&lt;номер&gt;.json).
        /// </summary>
        private static string GetRegistryPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var path1 = Path.Combine(baseDir, "tools", "pvc_bridge", "orders");
            if (Directory.Exists(path1)) return path1;

            var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            return Path.Combine(projectRoot, "tools", "pvc_bridge", "orders");
        }

        /// <summary>
        /// Id заказа из номера CCalc: повторный импорт обновляет тот же заказ,
        /// а не создаёт дубликат.
        /// </summary>
        private static string DeterministicOrderId(string number)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes("ccalc:" + number));
            return new Guid(hash).ToString();
        }

        private static string GetString(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? (p.GetString() ?? "")
                : "";

        private static double GetNumber(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0;

        /// <summary>
        /// Читает реестр и превращает сохранённые в CCalc заказы в заказы
        /// A.R.C. Frame: адрес, доп. КП с номером CCalc, москитная сетка и отлив.
        /// </summary>
        public static List<OrderData> LoadOrdersFromCcalcRegistry(string? registryDir = null)
        {
            var orders = new List<OrderData>();
            var dir = string.IsNullOrWhiteSpace(registryDir) ? GetRegistryPath() : registryDir;
            if (!Directory.Exists(dir)) return orders;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file, System.Text.Encoding.UTF8));
                    var root = doc.RootElement;

                    string number = GetString(root, "order_number");
                    if (string.IsNullOrWhiteSpace(number)) continue;

                    double total = GetNumber(root, "total_amount");
                    var order = new OrderData
                    {
                        Id = DeterministicOrderId(number),
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        ClientAddress = GetString(root, "address"),
                        Notes = $"Импорт из CCalc 9.12, заказ №{number}",
                        HasAdditionalKp = true,
                        AdditionalKpNumber = number,
                        AdditionalKpAmount = total,
                        AdditionalKps = new List<AdditionalKpItem>
                        {
                            new AdditionalKpItem { Number = number, Amount = total, IsActive = true }
                        },
                        TotalAmount = total
                    };

                    AddRegistryItem(order, root, "mosquito_net", "Anwis");
                    AddRegistryItem(order, root, "flashing", "Отлив");

                    orders.Add(order);
                }
                catch
                {
                    // Битый файл реестра не должен ломать импорт остальных.
                }
            }

            return orders.OrderByDescending(o => o.UpdatedAt).ToList();
        }

        private static void AddRegistryItem(OrderData order, JsonElement root, string key, string productName)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return;

            double w = GetNumber(el, "width_mm");
            double h = GetNumber(el, "length_mm");
            if (h <= 0) h = GetNumber(el, "height_mm");
            if (w <= 0 || h <= 0) return;

            order.Items.Add(new OrderItemData
            {
                Name = productName,
                Color = "Белый",
                Width = w,
                Height = h,
                Quantity = 1,
                Price = 0,
                IsActive = true,
                // Сетка приходит из CCalc уже в размере заказа: «Профипласт» —
                // единственный режим без пересчёта, иначе 445×1235 превращаются в 478×1280.
                AnwisSizeMode = productName == "Anwis" ? (int)AnwisSizeMode.Профипласт : 0
            });
        }

        /// <summary>
        /// Runs Python pipeline asynchronously on the given sketch image.
        /// </summary>
        public static async Task<PvcSketchResult> ProcessSketchAsync(string imagePath, bool dryRun = false)
        {
            if (!File.Exists(imagePath))
            {
                return new PvcSketchResult { Success = false, Error = $"Файл изображения не найден: {imagePath}" };
            }

            var scriptPath = GetScriptPath();
            if (!File.Exists(scriptPath))
            {
                return new PvcSketchResult { Success = false, Error = $"Скрипт моста не найден: {scriptPath}" };
            }

            var progPath = AppSettingsServiceAi.LoadCcalcProgramPath();
            var progArg = !string.IsNullOrWhiteSpace(progPath) && File.Exists(progPath)
                ? $" --prog-path \"{progPath}\""
                : "";

            var user = AppSettingsServiceAi.LoadCcalcUsername();
            var pass = AppSettingsServiceAi.LoadCcalcPassword();
            var authArg = $" --user \"{user}\" --password \"{pass}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\" \"{imagePath}\"{(dryRun ? " --dry-run" : "")}{progArg}{authArg}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";

            try
            {
                using var proc = new Process { StartInfo = psi };
                proc.Start();

                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();

                await Task.WhenAll(stdoutTask, stderrTask);
                await proc.WaitForExitAsync();

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (string.IsNullOrWhiteSpace(stdout))
                {
                    return new PvcSketchResult { Success = false, Error = $"Пустой ответ от моста. Ошибка: {stderr}" };
                }

                using var doc = JsonDocument.Parse(stdout);
                var root = doc.RootElement;

                if (!root.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
                {
                    var errMsg = root.TryGetProperty("error", out var err) ? err.GetString() : stderr;
                    return new PvcSketchResult { Success = false, Error = errMsg ?? "Ошибка выполнения расчета" };
                }

                var res = new PvcSketchResult { Success = true };

                if (root.TryGetProperty("recognized_data", out var rec))
                {
                    if (rec.TryGetProperty("address", out var a)) res.Address = a.GetString() ?? "";
                    if (rec.TryGetProperty("frame", out var fr))
                    {
                        if (fr.TryGetProperty("width_mm", out var w)) res.FrameWidth = w.GetInt32();
                        if (fr.TryGetProperty("height_mm", out var h)) res.FrameHeight = h.GetInt32();
                    }
                    if (rec.TryGetProperty("accessories", out var acc))
                    {
                        if (acc.TryGetProperty("flashing", out var fl))
                        {
                            if (fl.TryGetProperty("width_mm", out var fw)) res.DrainageWidth = fw.GetInt32();
                            if (fl.TryGetProperty("length_mm", out var flen)) res.DrainageLength = flen.GetInt32();
                        }
                        if (acc.TryGetProperty("windowsill", out var ws))
                        {
                            if (ws.TryGetProperty("width_mm", out var sw)) res.SillWidth = sw.GetInt32();
                            if (ws.TryGetProperty("length_mm", out var slen)) res.SillLength = slen.GetInt32();
                        }
                    }
                }

                if (root.TryGetProperty("calculation", out var calc))
                {
                    if (calc.TryGetProperty("order_number", out var on)) res.OrderNumber = on.GetString() ?? "";
                    if (calc.TryGetProperty("total_amount", out var ta)) res.TotalAmount = ta.GetDouble();

                    // Режим --build присылает адрес и размеры отлива/подоконника
                    // в блоке calculation (recognized_data может отсутствовать).
                    if (string.IsNullOrWhiteSpace(res.Address) &&
                        calc.TryGetProperty("address", out var cAddr))
                    {
                        res.Address = cAddr.GetString() ?? "";
                    }
                    if (calc.TryGetProperty("flashing", out var cFlash))
                    {
                        if (res.DrainageWidth == 0 && cFlash.TryGetProperty("width_mm", out var fw))
                            res.DrainageWidth = fw.GetInt32();
                        if (res.DrainageLength == 0 && cFlash.TryGetProperty("length_mm", out var fl))
                            res.DrainageLength = fl.GetInt32();
                    }
                    if (calc.TryGetProperty("windowsill", out var cSill))
                    {
                        if (res.SillWidth == 0 && cSill.TryGetProperty("width_mm", out var sw))
                            res.SillWidth = sw.GetInt32();
                        if (res.SillLength == 0 && cSill.TryGetProperty("length_mm", out var sl))
                            res.SillLength = sl.GetInt32();
                    }
                    if (calc.TryGetProperty("mosquito_net", out var mn))
                    {
                        if (mn.TryGetProperty("width_mm", out var mw)) res.NetWidth = mw.GetInt32();
                        if (mn.TryGetProperty("height_mm", out var mh)) res.NetHeight = mh.GetInt32();
                    }
                }

                return res;
            }
            catch (Exception ex)
            {
                return new PvcSketchResult { Success = false, Error = $"Исключение при вызове автопросчёта: {ex.Message}" };
            }
        }

        /// <summary>
        /// Injects the calculated PVC result directly into A.R.C. Frame order and additional KPs.
        /// </summary>
        public static void ApplyToOrder(
            PvcSketchResult result,
            CalculationViewModel calcVM,
            ClientInfo clientInfo,
            Action recalculateAndUpdateTotal)
        {
            if (result == null || !result.Success) return;

            // 1. Set Address
            if (!string.IsNullOrWhiteSpace(result.Address))
            {
                clientInfo.ClientAddress = result.Address;
            }

            // 2. Add Additional KP item for CCalc order
            if (result.TotalAmount > 0)
            {
                var orderNum = string.IsNullOrWhiteSpace(result.OrderNumber)
                    ? $"CCalc-{DateTime.Now:MMdd-HHmm}"
                    : result.OrderNumber;

                clientInfo.AdditionalKps.Add(new AdditionalKpItem
                {
                    Number = orderNum,
                    Amount = result.TotalAmount,
                    IsActive = true
                });
                clientInfo.HasAdditionalKp = true;
            }

            // 3. Add Mosquito Net (Anwis ПП) if dimensions are present
            if (result.NetWidth > 0 && result.NetHeight > 0)
            {
                // Профипласт — размер из CCalc сохраняется как есть (без +2/−30 и т.п.)
                calcVM.AddItem("Anwis", "Белый", result.NetWidth, result.NetHeight, 1, 0,
                    Models.AnwisSizeMode.Профипласт);
            }

            // 4. Add Flashing (Отлив) if length is present
            if (result.DrainageLength > 0)
            {
                var drainageW = result.DrainageWidth > 0 ? result.DrainageWidth : 130;
                calcVM.AddItem("Отлив", "Белый", drainageW, result.DrainageLength, 1, 0);
            }

            // 5. Recalculate order totals
            recalculateAndUpdateTotal?.Invoke();
        }

        /// <summary>
        /// Builds an interactive AI action plan from the recognized PVC sketch result.
        /// </summary>
        public static AiActionPlan BuildPlanFromPvcResult(PvcSketchResult result, string sourceUserText = "")
        {
            var reasoningParts = new System.Collections.Generic.List<string>();
            reasoningParts.Add("🪟 **Чертёж ПВХ-окна:**");
            if (!string.IsNullOrWhiteSpace(result.Address))
                reasoningParts.Add($"• **Адрес:** {result.Address}");
            if (result.FrameWidth > 0 && result.FrameHeight > 0)
                reasoningParts.Add($"• **Рама:** {result.FrameWidth} × {result.FrameHeight} мм (2 деления, створка П/О)");
            if (result.SillWidth > 0 && result.SillLength > 0)
                reasoningParts.Add($"• **Подоконник:** {result.SillWidth} × {result.SillLength} мм");
            if (result.DrainageLength > 0)
                reasoningParts.Add($"• **Отлив:** {(result.DrainageWidth > 0 ? result.DrainageWidth : 130)} × {result.DrainageLength} мм");
            if (result.NetWidth > 0 && result.NetHeight > 0)
                reasoningParts.Add($"• **Москитная сетка:** Anwis (ПП) {result.NetWidth} × {result.NetHeight} мм");
            if (result.TotalAmount > 0)
                reasoningParts.Add($"• **Заказ в CCalc 9.12:** №{result.OrderNumber} на сумму {result.TotalAmount:N0} ₽");
            else
                reasoningParts.Add("• **CCalc 9.12:** чертёж подготовлен к расчёту");

            var plan = new AiActionPlan
            {
                SourceUserText = sourceUserText,
                ReplyText = "Распознан чертёж ПВХ-окна. Проверьте параметры и подтвердите добавление в заказ:",
                Reasoning = string.Join("\n", reasoningParts),
                Mode = AiPlanMode.Plan,
                Status = AiPlanStatus.AwaitingConfirmation
            };

            // 1. Client Address
            if (!string.IsNullOrWhiteSpace(result.Address))
            {
                plan.Steps.Add(new AiActionStep
                {
                    CommandType = AiCommandType.SetClientInfo,
                    Params = new AiCommandParams { ClientAddress = result.Address },
                    PreviewText = $"📋 Установить адрес клиента: {result.Address}"
                });
            }

            // 2. Additional KP with CCalc Order
            if (result.TotalAmount > 0)
            {
                var orderNum = string.IsNullOrWhiteSpace(result.OrderNumber) ? $"CCalc-{DateTime.Now:MMdd-HHmm}" : result.OrderNumber;
                plan.Steps.Add(new AiActionStep
                {
                    CommandType = AiCommandType.AddAdditionalKp,
                    Params = new AiCommandParams
                    {
                        AdditionalKpNumber = orderNum,
                        AdditionalKpAmount = result.TotalAmount
                    },
                    PreviewText = $"📋 Записать в доп. КП: CCalc №{orderNum} на сумму {result.TotalAmount:N0} ₽"
                });
            }

            // 3. Flashing (Отлив)
            if (result.DrainageLength > 0)
            {
                int w = result.DrainageWidth > 0 ? result.DrainageWidth : 130;
                plan.Steps.Add(new AiActionStep
                {
                    CommandType = AiCommandType.AddItem,
                    Params = new AiCommandParams
                    {
                        Type = "Отлив",
                        Color = "Белый",
                        Width = w,
                        Height = result.DrainageLength,
                        Quantity = 1,
                        Price = 0
                    },
                    PreviewText = $"Добавить Отлив, Белый, {w}×{result.DrainageLength} мм"
                });
            }

            // 4. Mosquito Net (Anwis ПП)
            if (result.NetWidth > 0 && result.NetHeight > 0)
            {
                plan.Steps.Add(new AiActionStep
                {
                    CommandType = AiCommandType.AddItem,
                    Params = new AiCommandParams
                    {
                        Type = "Anwis",
                        Color = "Белый",
                        Width = result.NetWidth,
                        Height = result.NetHeight,
                        Quantity = 1,
                        Price = 0,
                        AnwisMode = AnwisSizeMode.Профипласт
                    },
                    PreviewText = $"Добавить Anwis (ПП), Белый, {result.NetWidth}×{result.NetHeight} мм"
                });
            }

            return plan;
        }
    }
}
