using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    public enum RouteKind { Unknown, Info, Undo, Redo, ClearPlan, Explain }
    public enum ExplainTarget { None, Last, All, Index }

    public sealed class RouteResult
    {
        public bool IsHandled { get; init; }
        public RouteKind Kind { get; init; } = RouteKind.Info;
        public string Message { get; init; } = "";
        public List<AiCommand> Commands { get; } = new();
        public ExplainTarget ExplainTarget { get; init; } = ExplainTarget.None;
        public int ExplainIndex { get; init; }
    }

    /// <summary>A slash command shown in the autocomplete popup and /help.</summary>
    public sealed record SlashCommandInfo(string Command, string Description, string Aliases = "")
    {
        /// <summary>True when <paramref name="query"/> matches the command or one of its aliases.</summary>
        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            return Command.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                || Aliases.Split(',')
                    .Select(a => a.Trim())
                    .Where(a => a.Length > 0)
                    .Any(a => a.StartsWith(query, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Deterministic, offline slash commands. Every route works without the
    /// network and spends zero tokens; mutating ones produce commands that go
    /// through the normal plan → preview → confirmation pipeline.
    /// </summary>
    public static class AiLocalCommandRouter
    {
        /// <summary>
        /// Canonical command catalog — single source of truth for the autocomplete
        /// popup (AiAssistantControl) and the /help text below.
        /// </summary>
        public static IReadOnlyList<SlashCommandInfo> Commands { get; } = new List<SlashCommandInfo>
        {
            new("/товары", "Каталог товаров", "/products"),
            new("/цены", "Цены каталога", "/prices"),
            new("/итоги", "Итоги текущего заказа", "/totals"),
            new("/статус", "Заказ, модель, статистика сессии", "/status"),
            new("/последняя", "Подробности последней позиции", "/last"),
            new("/отменить", "Отменить последнее действие (Ctrl+Z)", "/undo"),
            new("/повторить", "Повторить отменённое (Ctrl+Y)", "/redo"),
            new("/очистить", "Очистить расчёт (с подтверждением)", "/clear"),
            new("/объясни [последнюю|позицию N|всё]", "Объяснить расчёт", "/explain"),
            new("/замер", "Правила замера сеток Anwis и светового проёма", "/anwis, /measure"),
            new("/крепления", "Справка по Z-креплениям и зацепам", "/brackets"),
            new("/faq", "База знаний и частые вопросы (офлайн)", "/kb"),
            new("/помощь", "Список всех команд", "/help, /helpme")
        };

        public static string HelpText => BuildHelpText();

        private static string BuildHelpText()
        {
            var sb = new StringBuilder("**Локальные команды** (работают без интернета):\n");
            foreach (var c in Commands)
                sb.AppendLine($"• `{c.Command}` — {c.Description}");
            sb.AppendLine("Английские варианты: /products /prices /totals /status /last /undo /redo /clear /explain /help.");
            return sb.ToString();
        }

        public static RouteResult TryRoute(
            string input,
            AiOrderContext? order,
            string? currentModel = null,
            AiSessionSummary? session = null)
        {
            var text = input?.Trim() ?? "";
            if (!text.StartsWith('/')) return new RouteResult { IsHandled = false };

            // Split into command word + arguments.
            int sp = text.IndexOf(' ');
            var cmd = (sp < 0 ? text : text[..sp]).Trim().ToLowerInvariant();
            var args = sp < 0 ? "" : text[(sp + 1)..].Trim();

            switch (cmd)
            {
                case "/товары" or "/products":
                    return Info(ProductsText());

                case "/цены" or "/prices":
                    return Info(PricesText());

                case "/итоги" or "/totals":
                    return Info(TotalsText(order));

                case "/статус" or "/status":
                    return Info(StatusText(order, currentModel, session));

                case "/последняя" or "/last":
                    return Info(LastItemText(order));

                case "/отменить" or "/undo":
                    return new RouteResult { IsHandled = true, Kind = RouteKind.Undo, Message = "↩ Отменяю последнее действие…" };

                case "/повторить" or "/redo":
                    return new RouteResult { IsHandled = true, Kind = RouteKind.Redo, Message = "↪ Повторяю отменённое действие…" };

                case "/очистить" or "/clear":
                {
                    if (order is not { Count: > 0 })
                        return Info("Заказ уже пуст — очищать нечего.");

                    var r = new RouteResult
                    {
                        IsHandled = true,
                        Kind = RouteKind.ClearPlan,
                        Message = $"Будет удалено {order.Count} позиций на сумму {MoneyFormatService.Format(order.Total)} ₽. Действие можно отменить через Undo."
                    };
                    r.Commands.Add(new AiCommand { Type = AiCommandType.ClearAll });
                    return r;
                }

                case "/объясни" or "/explain":
                    return ExplainRoute(args);

                case "/замер" or "/anwis" or "/measure":
                    return Info(AnwisMeasurementText());

                case "/крепления" or "/brackets":
                    return Info(BracketsText());

                case "/faq" or "/kb":
                    return Info(FaqText());

                case "/помощь" or "/help" or "/helpme":
                    return Info(HelpText);

                default:
                    return new RouteResult
                    {
                        IsHandled = true,
                        Kind = RouteKind.Info,
                        Message = $"Неизвестная команда «{cmd}». Доступные команды:\n" + HelpText
                    };
            }
        }

        private static string AnwisMeasurementText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## 📐 Правила замера и расчёта москитных сеток Anwis (Вставные / Крыло)");
            sb.AppendLine();
            sb.AppendLine("Сетки Anwis устанавливаются в световой проём створки изнутри помещения без сверления рамы.");
            sb.AppendLine();
            sb.AppendLine("### Режимы расчёта в A.R.C. Frame:");
            sb.AppendLine("1. **Световой проём (Проём)** — чистый размер между внутренними кромками коробки окна (от резинки до резинки при открытой створке).");
            sb.AppendLine("   • Габарит сетки вычисляется автоматически прибавлением ширины полочки профиля крыла (+25 мм по периметру).");
            sb.AppendLine("2. **Габарит** — окончательный размер рамки сетки по внешним краям.");
            sb.AppendLine("3. **ББ60 (Базовый брус 60 мм)** — замер по фальцу для типовых 60-мм систем (Rehau Euro 60, KBE 58/60, Veka Euroline).");
            sb.AppendLine("4. **ББ70 (Базовый брус 70 мм)** — замер по фальцу для широких 70–82 мм профилей (Rehau Grazio/Delight, KBE 70, Veka Softline).");
            sb.AppendLine();
            sb.AppendLine("💡 **Рекомендация по замеру:** меряйте световой проём в трёх точках (сверху, посередине, снизу) и берите минимальное значение.");
            return sb.ToString();
        }

        private static string BracketsText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## 🔩 Справка по типам креплений москитных сеток");
            sb.AppendLine();
            sb.AppendLine("• **Поворотные Z-зацепы (крючки Anwis):** металлические лепестки на заклёпках. Заводятся за наплав рамы изнутри. Доступны размеры зацепов от 3 до 26 мм в зависимости от толщины наплава с уплотнителем.");
            sb.AppendLine("• **Пластиковые Z-уголки (стандартный наружный монтаж):** пара нижних (малые) и пара верхних (удлинённые) уголков, прикручиваемых на саморезы к внешней стороне рамы.");
            sb.AppendLine("• **Металлические Z-крепления:** усиленный аналог пластиковых уголков из окрашенной нержавеющей стали (рекомендуется для верхних этажей и ветровых нагрузок).");
            sb.AppendLine("• **Плунжерные крепления (штоки с пружиной):** используются при невозможности установить наружные уголки (решётки, четверти вплотную).");
            return sb.ToString();
        }

        private static string FaqText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## 📚 База знаний и частые вопросы (FAQ)");
            sb.AppendLine();
            sb.AppendLine("• **В чём разница между полотнами FiberGlass, Антикошка (PetScreen) и Антипыль (Poll-Tex)?**");
            sb.AppendLine("  - *FiberGlass (Стандарт):* серое стекловолокно с ПВХ покрытием, ячейка 1.2×1.2 мм.");
            sb.AppendLine("  - *Антикошка (PetScreen):* сверхпрочные полиэстеровые нити в 3 раза толще стандарта, выдерживают когти и вес животных.");
            sb.AppendLine("  - *Антипыль / Антипыльца:* мелкая ячейка 0.25×1.0 мм, задерживает аллергены, дорожную пыль и мошек.");
            sb.AppendLine();
            sb.AppendLine("• **Как обозначаются изделия на чертежах?**");
            sb.AppendLine("  - **«#»** (штриховка / решётка) — москитная сетка.");
            sb.AppendLine("  - **«СП»** / **«с/п»** — стеклопакет (глухое остекление, не сетка!).");
            sb.AppendLine();
            sb.AppendLine("• **Команды управления:** /товары, /цены, /итоги, /замер, /крепления, /отменить (Ctrl+Z).");
            return sb.ToString();
        }

        private static RouteResult ExplainRoute(string args)
        {
            var a = args.ToLowerInvariant();
            if (a.Contains("всё") || a.Contains("all"))
                return new RouteResult { IsHandled = true, Kind = RouteKind.Explain, ExplainTarget = ExplainTarget.All };
            if (a.Contains("последн") || a.Contains("last"))
                return new RouteResult { IsHandled = true, Kind = RouteKind.Explain, ExplainTarget = ExplainTarget.Last };

            // «позицию 3» / «позиция 3» / «3»
            var digits = new string(a.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out int idx) && idx > 0)
                return new RouteResult { IsHandled = true, Kind = RouteKind.Explain, ExplainTarget = ExplainTarget.Index, ExplainIndex = idx };

            return new RouteResult
            {
                IsHandled = true,
                Kind = RouteKind.Explain,
                ExplainTarget = ExplainTarget.Last,
                Message = "Не понял аргумент — объясняю последнюю позицию. Варианты: /объясни последнюю, /объясни позицию N, /объясни всё."
            };
        }

        private static RouteResult Info(string message)
            => new() { IsHandled = true, Kind = RouteKind.Info, Message = message };

        // ── Text builders (all local, zero tokens) ──────────────

        private static string TotalsText(AiOrderContext? order)
        {
            if (order == null || order.Count == 0)
                return "Расчёт пуст. Добавьте позиции или спросите AI: «Сделай сетку 700×1400 бб60 белую».";

            var sb = new StringBuilder();
            sb.AppendLine("## Итоги заказа");
            sb.AppendLine(order.FormatBrief());
            foreach (var g in order.GroupsByCategory)
                sb.AppendLine($"• {g.Key}: {g.Count} поз. — {MoneyFormatService.Format(g.Total)} ₽");
            sb.AppendLine();
            sb.AppendLine("Подробные позиции — запросите AI: «Покажи текущий заказ».");
            return sb.ToString();
        }

        private static string StatusText(AiOrderContext? order, string? model, AiSessionSummary? session)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Статус");
            if (order != null && order.Count > 0)
                sb.AppendLine(order.FormatBrief());
            else
                sb.AppendLine("Расчёт пуст.");
            if (!string.IsNullOrWhiteSpace(model))
                sb.AppendLine($"Модель: {model}");
            var configuredProviders = new[] { AiProvider.OpenRouter, AiProvider.Nvidia, AiProvider.FreeLlmApi }
                .Where(provider => !string.IsNullOrWhiteSpace(AiKeyValidator.GetApiKey(provider)))
                .Select(AiKeyValidator.ProviderName)
                .ToList();
            sb.AppendLine(configuredProviders.Count > 0
                ? $"Провайдеры: {string.Join(" + ", configuredProviders)}"
                : "Провайдеры: API-ключи не настроены");
            if (session is { Requests: > 0 })
                sb.AppendLine(session.FormatBrief());
            else
                sb.AppendLine("Статистика сессии: запросов ещё не было.");
            return sb.ToString();
        }

        private static string LastItemText(AiOrderContext? order)
        {
            if (order == null || order.Count == 0)
                return "Заказ пуст — последней позиции нет.";
            var it = order.Items[^1];
            var sb = new StringBuilder();
            sb.AppendLine($"## Последняя позиция (#{it.Index})");
            sb.AppendLine(DescribeItem(it));
            return sb.ToString();
        }

        private static string DescribeItem(AiOrderItemInfo it)
        {
            var parts = new List<string>();
            if (it.IsAreaBased || it.IsPerLinearMeter)
                parts.Add($"{Fmt(it.WidthInput)}×{Fmt(it.HeightInput)} мм (расчёт {Fmt(it.WidthCalc)}×{Fmt(it.HeightCalc)})");
            if (it.IsManualPiece) parts.Add("ручная позиция");
            parts.Add($"{Fmt(it.Quantity)} шт.");
            if (!string.IsNullOrWhiteSpace(it.AnwisModeLabel)) parts.Add($"Anwis: {it.AnwisModeLabel}");
            if (it.IsInstallationApplicable) parts.Add($"монтаж: {it.InstallationLabel}");
            return $"{it.Name} {it.Color}: {string.Join(", ", parts)}. Цена {MoneyFormatService.Format(it.Price)} ₽/{it.Unit}, итог {MoneyFormatService.Format(it.TotalWithInstall)} ₽.";
        }

        private static string ProductsText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Каталог товаров");
            foreach (var group in ProductCatalog.UserGroups)
            {
                sb.AppendLine($"**{group.Name}**" + (string.IsNullOrWhiteSpace(group.Subtitle) ? "" : $" — {group.Subtitle}"));
                foreach (var p in group.Products)
                    sb.AppendLine($"• {p}");
            }
            sb.AppendLine();
            sb.AppendLine("Подробные цены: /цены. Добавление: «Сделай сетку Anwis 700×1400 бб60 белую».");
            return sb.ToString();
        }

        private static string PricesText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Цены каталога");
            foreach (var group in ProductCatalog.UserGroups)
            {
                sb.AppendLine($"**{group.Name}**");
                foreach (var p in group.Products)
                {
                    if (ProductCatalog.IsManualPiece(p))
                    {
                        sb.AppendLine($"• {p} — цена вводится вручную");
                        continue;
                    }
                    var unit = ProductCatalog.IsPerLinearMeter(p) ? "м.п." : "м²";
                    var colors = AiPlanValidator.KnownColors.TryGetValue(p, out var c) ? c : Array.Empty<string>();
                    var basePrice = AiCommandParser.GetDefaultPrice(p, colors.FirstOrDefault() ?? "");
                    var colorNote = colors.Length > 1
                        ? $" (цвета: {string.Join(", ", colors)})"
                        : "";
                    sb.AppendLine($"• {p} — от {basePrice:N0} ₽/{unit}{colorNote}");
                }
            }
            sb.AppendLine();
            sb.AppendLine("Цены в заказе берутся из каталога программы; AI их не изменяет.");
            return sb.ToString();
        }

        private static string Fmt(double v) => v == Math.Floor(v) ? ((int)v).ToString() : v.ToString("0.##");
    }
}
