using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Builds <see cref="AiActionPlan"/> objects from parsed commands and
    /// decides the confirmation policy. Local, deterministic, offline —
    /// never depends on the LLM.
    /// </summary>
    public static class AiPlanBuilder
    {
        /// <summary>
        /// Confirmation policy per command type. Read-only / overlay-opening
        /// commands (list products, calc slope) run immediately; every
        /// mutating command requires an explicit preview + confirmation.
        /// </summary>
        public static bool RequiresConfirmation(AiCommand command) => command.Type switch
        {
            AiCommandType.AddItem or AiCommandType.DeleteLast or AiCommandType.DeleteItems
                or AiCommandType.UpdateItems or AiCommandType.ClearAll
                or AiCommandType.UpdateItemByIndex or AiCommandType.DeleteItemByIndex
                or AiCommandType.ToggleItemActive or AiCommandType.SetClientInfo
                or AiCommandType.ApplyTemplate or AiCommandType.SaveOrder
                or AiCommandType.PrintKp or AiCommandType.ExportPdf => true,
            _ => false
        };

        /// <summary>Confirmation is required when any step is mutating.</summary>
        public static bool RequiresConfirmation(IReadOnlyList<AiCommand> commands)
            => commands.Any(RequiresConfirmation);

        public static AiActionPlan FromCommand(
            AiCommand command,
            string? sourceUserText = null,
            string reply = "",
            AiPlanMode mode = AiPlanMode.Plan)
            => FromCommands(new[] { command }, sourceUserText, reply, mode);

        public static AiActionPlan FromCommands(
            IReadOnlyList<AiCommand> commands,
            string? sourceUserText = null,
            string reply = "",
            AiPlanMode mode = AiPlanMode.Plan)
        {
            var plan = new AiActionPlan
            {
                SourceUserText = sourceUserText ?? "",
                ReplyText = reply,
                Mode = mode,
                IsReadOnly = commands.All(c => c.Type is AiCommandType.ListProducts)
            };

            foreach (var c in commands)
            {
                plan.Steps.Add(new AiActionStep
                {
                    CommandType = c.Type,
                    Params = c.Params,
                    PreviewText = BuildStepPreview(c)
                });
            }

            plan.RequiresConfirmation = mode == AiPlanMode.Plan
                && commands.Any(RequiresConfirmation);
            plan.Status = plan.RequiresConfirmation
                ? AiPlanStatus.ReadyForPreview
                : AiPlanStatus.Draft;
            return plan;
        }

        /// <summary>Human-readable preview line for a plan step.</summary>
        public static string BuildStepPreview(AiCommand c)
        {
            var p = c.Params;
            return c.Type switch
            {
                AiCommandType.AddItem => BuildAddPreview(c),
                AiCommandType.DeleteLast => "🗑 Удалить последнюю позицию",
                AiCommandType.DeleteItems => BuildDeletePreview(p.TargetProduct),
                AiCommandType.ClearAll => "🗑 Очистить весь расчёт",
                AiCommandType.ListProducts => "📋 Показать список товаров",
                AiCommandType.CalcSlope => $"🏗 Просчёт откосов: {p.Width}×{p.Height} мм, глубина {p.Depth} мм, {p.Quantity:0.##} отк.",
                AiCommandType.UpdateItems => BuildUpdatePreview(c),
                AiCommandType.UpdateItemByIndex => BuildUpdateItemByIndexPreview(c),
                AiCommandType.DeleteItemByIndex => $"🗑 Удалить строку #{p.ItemIndex}",
                AiCommandType.ToggleItemActive => $"👁 {(p.IsActive == false ? "Отключить" : "Включить")} строку #{p.ItemIndex}",
                AiCommandType.SetClientInfo => BuildSetClientInfoPreview(p),
                AiCommandType.ApplyTemplate => $"📐 Применить шаблон «{p.TemplateId}»{(p.Width > 0 && p.Height > 0 ? $" ({p.Width}×{p.Height} мм)" : "")}",
                AiCommandType.SaveOrder => "💾 Сохранить заказ",
                AiCommandType.PrintKp => "🖨 Печать КП",
                AiCommandType.ExportPdf => "📄 Экспорт КП в PDF",
                _ => "✅ Действие"
            };
        }

        private static string BuildAddPreview(AiCommand c)
        {
            var p = c.Params;
            var parts = new List<string> { p.Type };
            if (p.IsAnticat == true) parts.Add("Антикошка");
            if (!string.IsNullOrWhiteSpace(p.Color)) parts.Add(p.Color);
            bool needsSizes = !ProductCatalog.IsManualPiece(p.Type) && !p.IsCustomProduct;
            // «Свой товар»: only show sizes the user actually entered.
            if (needsSizes && p.Width > 0 && p.Height > 0)
                parts.Add($"{p.Width}×{p.Height} мм");
            parts.Add($"{FormatQty(p.Quantity)} шт.");
            if (ProductCatalog.IsAreaBased(p.Type) && needsSizes)
                parts.Add($"{p.Price:N0} ₽");
            if (AnwisSizeService.IsApplicable(p.Type))
                parts.Add(AiCommandParser.AnwisModeLabel(p.AnwisMode));
            parts.Add(p.InstallationMode switch
            {
                0 => "с монтажом",
                1 => "без монтажа",
                2 => "в конструкцию",
                _ => ""
            });
            var text = "➕ Добавить: " + string.Join(", ", parts.Where(x => x.Length > 0));
            // «Количество: 1» is a safe default — make the preview honest about it.
            if (Math.Abs(p.Quantity - 1) < 0.001)
                text += " (количество по умолчанию)";
            return text;
        }

        private static string BuildDeletePreview(string target)
            => string.IsNullOrWhiteSpace(target)
                ? "🗑 Удалить позиции (фильтр не указан — все)"
                : $"🗑 Удалить позиции: «{target}»";

        private static string BuildUpdatePreview(AiCommand c)
        {
            var p = c.Params;
            var target = string.IsNullOrWhiteSpace(p.TargetProduct)
                ? "все позиции"
                : $"«{p.TargetProduct}»";
            var parts = new List<string>();
            if (p.UpdateInstallationMode.HasValue)
                parts.Add($"монтаж → {p.UpdateInstallationMode.Value switch { 0 => "включён", 1 => "без монтажа", _ => "в конструкцию" }}");
            if (p.UpdateAnwisMode.HasValue)
                parts.Add($"Anwis → {AiCommandParser.AnwisModeLabel(p.UpdateAnwisMode.Value)}");
            if (p.UpdateColor != null)
                parts.Add($"цвет → {p.UpdateColor}");
            if (p.UpdateInstallationAmount.HasValue)
                parts.Add($"сумма монтажа → {p.UpdateInstallationAmount.Value:N0} ₽");
            if (p.UpdatePrice.HasValue)
                parts.Add($"цена → {p.UpdatePrice.Value:N0} ₽");
            return "🔄 Изменить " + target + ": " + (parts.Count > 0 ? string.Join(", ", parts) : "—");
        }

        private static string BuildUpdateItemByIndexPreview(AiCommand c)
        {
            var p = c.Params;
            var parts = new List<string>();
            if (p.UpdateWidth.HasValue || p.UpdateHeight.HasValue)
                parts.Add($"размеры → {(p.UpdateWidth?.ToString() ?? "—")}×{(p.UpdateHeight?.ToString() ?? "—")} мм");
            if (p.UpdateQuantity.HasValue) parts.Add($"кол-во → {FormatQty(p.UpdateQuantity.Value)} шт.");
            if (p.UpdatePrice.HasValue) parts.Add($"цена → {p.UpdatePrice.Value:N0} ₽");
            if (p.UpdateColor != null) parts.Add($"цвет → {p.UpdateColor}");
            if (p.UpdateInstallationMode.HasValue) parts.Add($"монтаж → {p.UpdateInstallationMode.Value switch { 0 => "включён", 1 => "без монтажа", _ => "в конструкцию" }}");
            if (p.UpdateAnwisMode.HasValue) parts.Add($"Anwis → {AiCommandParser.AnwisModeLabel(p.UpdateAnwisMode.Value)}");
            if (p.IsAnticat.HasValue) parts.Add($"антикошка → {(p.IsAnticat.Value ? "да" : "нет")}");
            if (p.IsActive.HasValue) parts.Add($"активность → {(p.IsActive.Value ? "включена" : "отключена")}");
            return $"🔄 Изменить строку #{p.ItemIndex}: " + (parts.Count > 0 ? string.Join(", ", parts) : "—");
        }

        private static string BuildSetClientInfoPreview(AiCommandParams p)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(p.ClientPhone)) parts.Add($"тел: {p.ClientPhone}");
            if (!string.IsNullOrWhiteSpace(p.ClientAddress)) parts.Add($"адрес: {p.ClientAddress}");
            if (!string.IsNullOrWhiteSpace(p.ContractNumber)) parts.Add($"договор: {p.ContractNumber}");
            if (!string.IsNullOrWhiteSpace(p.Notes)) parts.Add($"примечание: {p.Notes}");
            return "📋 Данные клиента: " + (parts.Count > 0 ? string.Join(", ", parts) : "обновление");
        }

        private static string FormatQty(double q)
            => q == Math.Floor(q) ? ((int)q).ToString() : q.ToString("0.##");
    }
}
