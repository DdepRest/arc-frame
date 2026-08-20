using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
                    // Plan-mode contract: { mode, reply, requires_confirmation, steps[] }.
                    // Steps may contain several actions → the whole reply is routed
                    // through the plan → preview → confirm → execute pipeline.
                    if (root.TryGetProperty("steps", out _) || root.TryGetProperty("mode", out _))
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
            var type = GetStr(p, "type") ?? "Anwis"; var color = GetStr(p, "color") ?? "";
            var w = GetInt(p, "width"); var h = GetInt(p, "height");
            var q = GetDouble(p, "quantity", 1); var price = GetDouble(p, "price");
            var mode = AnwisSizeService.DefaultMode; var modeStr = GetStr(p, "anwis_mode");
            if (Services.AnwisSizeService.IsApplicable(type) && string.IsNullOrEmpty(modeStr))
                return (null, "⚠ Для Anwis укажите режим: ББ60, ББ70, ПП, Проём или Габарит.");
            if (!string.IsNullOrEmpty(modeStr))
                mode = ParseAnwisModeString(modeStr);
            var im = ParseInstallationMode(p);
            if (price <= 0) price = GetDefaultPrice(type, color);
            return (new AiCommand { Type = AiCommandType.AddItem, Params = new AiCommandParams { Type = type, Color = color, Width = w, Height = h, Quantity = q, Price = price, AnwisMode = mode, InstallationMode = im } }, null);
        }

        private static AnwisSizeMode ParseAnwisModeString(string s) => s.ToLowerInvariant() switch
        {
            "бб60" or "bb60" or "брусбокс60" or "брусбокс 60" => AnwisSizeMode.Брусбокс60,
            "бб70" or "bb70" or "брусбокс70" or "брусбокс 70" => AnwisSizeMode.Брусбокс70,
            "пп" or "pp" or "профипласт" => AnwisSizeMode.Профипласт,
            "проём" or "проем" or "размер проёма" => AnwisSizeMode.РазмерПроёма,
            "габарит" or "габаритный" => AnwisSizeMode.Габаритный,
            _ => AnwisSizeService.DefaultMode
        };

        private static (AiCommand? Command, string? ReplyOverride) ParseUpdateItems(JsonElement root)
        {
            var p = root.TryGetProperty("params", out var pp) ? pp : root;
            var target = GetStr(p, "product") ?? GetStr(p, "target") ?? "";
            int? installMode = null; double? price = null; double? installAmount = null; AnwisSizeMode? anwisMode = null; string? updateColor = null;
            bool hasAny = false;

            if (p.TryGetProperty("installation_mode", out var im)) { installMode = ParseInstallationModeField(im); if (installMode.HasValue) hasAny = true; }
            if (p.TryGetProperty("price", out var pr)) { if (pr.ValueKind == JsonValueKind.Number) { price = pr.GetDouble(); if (price > 0) hasAny = true; } }
            if (p.TryGetProperty("installation_amount", out var ia) || p.TryGetProperty("install_amount", out ia)) { if (ia.ValueKind == JsonValueKind.Number) { installAmount = ia.GetDouble(); hasAny = true; } }
            if (p.TryGetProperty("anwis_mode", out var am)) { if (am.ValueKind == JsonValueKind.String) { anwisMode = ParseAnwisModeString(am.GetString()!); hasAny = true; } }
            if (p.TryGetProperty("color", out var cl)) { if (cl.ValueKind == JsonValueKind.String) { updateColor = cl.GetString(); hasAny = true; } }

            if (!hasAny) return (null, "⚠ Укажите, что нужно изменить: installation_mode (0/1/2), price, installation_amount или anwis_mode.");

            return (new AiCommand { Type = AiCommandType.UpdateItems, Params = new AiCommandParams { TargetProduct = target, UpdateInstallationMode = installMode, UpdatePrice = price, UpdateInstallationAmount = installAmount, UpdateAnwisMode = anwisMode, UpdateColor = updateColor } }, null);
        }

        private static int? ParseInstallationModeField(JsonElement v)
        {
            if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt32(out int n) && n >= 0 && n <= 2 ? n : null;
            if (v.ValueKind == JsonValueKind.String)
                return v.GetString()?.Trim().ToLowerInvariant() switch { "2" or "в конструкцию" or "в конструцию" or "конструкция" => 2, "1" or "без монтажа" or "без установки" or "без" or "не нужно" => 1, "0" or "монтаж включён" or "монтаж включен" or "включён" or "включен" or "с монтажом" or "с монтажём" or "монтаж" => 0, _ => null };
            return null;
        }

        private static int ParseInstallationMode(JsonElement p) => !p.TryGetProperty("installation_mode", out var v) ? -1 : ParseInstallationModeField(v) ?? -1;

        /// <summary>
        /// Catalog fallback price used when the model doesn't supply one.
        /// Public so the clarification form can price items the same way.
        /// </summary>
        public static double GetDefaultPrice(string type, string color) => type switch
        {
            "Anwis" when color.Contains("Коричневый", StringComparison.OrdinalIgnoreCase) => 1900, "Anwis" => 1800,
            "На навесах" when color.Contains("Коричневый", StringComparison.OrdinalIgnoreCase) => 3000, "На навесах" => 2900,
            "Оконная на метал. крепл." when color.Contains("Коричневый", StringComparison.OrdinalIgnoreCase) => 3300, "Оконная на метал. крепл." => 3200,
            "Дверная сетка" => 3000, "Отлив" when color.Contains("Золотой", StringComparison.OrdinalIgnoreCase) => 2650, "Отлив" => 2150,
            "Козырёк" when color.Contains("Золотой", StringComparison.OrdinalIgnoreCase) => 2650, "Козырёк" => 2150,
            "Короб" when color.Contains("Золотой", StringComparison.OrdinalIgnoreCase) => 2650, "Короб" => 2150,
            "ПСУЛ" => 100, "Уплотнение" => 250, _ => 0
        };

        // ── JSON extraction ──────────────────────────────────────

        private static string? ExtractJson(string content)
        {
            int s = content.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (s >= 0) { s += 7; int e = content.IndexOf("```", s); if (e > s) return content[s..e].Trim(); }
            s = content.IndexOf("```\n"); if (s < 0) s = content.IndexOf("```\r\n");
            if (s >= 0) { s += 3; int e = content.IndexOf("```", s); if (e > s) { var c = content[s..e].Trim(); if (c.StartsWith("{")) return c; } }
            s = content.IndexOf('{');
            if (s >= 0) { int d = 0; for (int i = s; i < content.Length; i++) { if (content[i] == '{') d++; else if (content[i] == '}') d--; if (d == 0) { var c = content[s..(i + 1)]; if (c.Contains("\"action\"") || c.Contains("\"mode\"") || c.Contains("\"steps\"")) return c; break; } } }
            return null;
        }

        private static string ExtractReplyText(string content) { var r = content; while (true) { int s = r.IndexOf("```json", StringComparison.OrdinalIgnoreCase); if (s < 0) break; int e = r.IndexOf("```", s + 7); if (e < 0) break; r = r.Remove(s, e + 3 - s); } return r.Trim(); }

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
            _ => "✅ Готово."
        };


        private static string BuildDeleteItemsConfirmation(AiCommand c) { var t = string.IsNullOrWhiteSpace(c.Params.TargetProduct) ? "все позиции" : $"«{c.Params.TargetProduct}»"; return $"🗑 Удалены позиции: {t}"; }
        private static string BuildAddItemConfirmation(AiCommand c) { var p = c.Params; var m = $"✅ Добавлено: {p.Type} {p.Color} {p.Width}×{p.Height} мм, {p.Quantity} шт. по {p.Price:N0} ₽"; return p.InstallationMode switch { 0 => $"{m}, монтаж включён", 1 => $"{m}, без монтажа", 2 => $"{m}, в конструкцию", _ => m }; }

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

        internal static string AnwisModeLabel(AnwisSizeMode m) => m switch { AnwisSizeMode.Брусбокс60 => "ББ60", AnwisSizeMode.Брусбокс70 => "ББ70", AnwisSizeMode.Профипласт => "ПП", AnwisSizeMode.РазмерПроёма => "Проём", AnwisSizeMode.Габаритный => "Габарит", _ => m.ToString() };

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
    }
}
