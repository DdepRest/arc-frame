using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Пустые состояния рисуются из общих блоков <c>EmptyState.*</c> (v3.53.0).
    ///
    /// До этой версии «пусто» собиралось шесть раз шестью разными наборами
    /// чисел: глиф 22 / 40 / 44 / 48 / 60px, прозрачность 0.30 / 0.35 / 0.60 /
    /// 0.85, заголовок то TextPrimary, то TextMuted, то TextSecondary, подсказка
    /// то 11, то 12px. Один и тот же смысл выглядел на разных экранах по-разному,
    /// и каждый новый экран добавлял седьмой вариант. Тесты держат инвариант:
    /// блоки берутся из словаря, словарь берёт только токены.
    /// </summary>
    public class EmptyStateTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator", "App.xaml")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Не найден корень репозитория.");
        }

        private static string AppDir => Path.Combine(RepoRoot(), "MosquitoNetCalculator");

        private static string Read(string relativePath) =>
            File.ReadAllText(Path.Combine(AppDir, relativePath));

        /// <summary>Файлы, в которых живут состояния пустоты (по x:Name контейнера).</summary>
        private static readonly string[] StateHosts =
        {
            "Controls/OrderItemsControl.xaml",      // «Выберите товар» — сетка позиций
            "Controls/OrdersHistoryControl.xaml",   // «Заказов пока нет» + «Ничего не найдено»
            "Controls/PricesControl.xaml",          // «Справочник цен пуст»
            "Controls/UpdatesTabControl.xaml",      // «Ничего не найдено» (фильтр/поиск)
            "Controls/AdminPanelControl.xaml",      // «Отчёты ещё не получены»
        };

        private static readonly string[] Blocks =
        {
            "EmptyState.Panel", "EmptyState.Icon", "EmptyState.Title", "EmptyState.Hint",
        };

        [Fact]
        public void EveryEmptyState_IsBuiltFromTheSharedBlocks()
        {
            var missing = new List<string>();

            foreach (string file in StateHosts)
            {
                string text = Read(file);
                foreach (string block in Blocks)
                {
                    if (!text.Contains($"EmptyState.{block.Split('.')[1]}", StringComparison.Ordinal))
                        missing.Add($"{file}: не использует {block}");
                }
            }

            Assert.True(missing.Count == 0,
                "Состояние пустоты собирается «на глазок» — используй общие блоки из " +
                "Themes/EmptyStateStyles.xaml:\n  " + string.Join("\n  ", missing));
        }

        [Fact]
        public void EmptyStateDictionary_IntroducesNoLiterals()
        {
            string path = Path.Combine(AppDir, "Themes", "EmptyStateStyles.xaml");
            Assert.True(File.Exists(path), "Themes/EmptyStateStyles.xaml отсутствует.");

            string text = Regex.Replace(File.ReadAllText(path), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

            var offenders = new List<string>();
            if (Regex.IsMatch(text, "FontSize=\"[0-9.]+\"")) offenders.Add("литеральный FontSize");
            if (Regex.IsMatch(text, "CornerRadius=\"[0-9]")) offenders.Add("литеральный CornerRadius");
            if (Regex.IsMatch(text, "#[0-9A-Fa-f]{6}\\b")) offenders.Add("hex-литерал");
            if (Regex.IsMatch(text, "Duration=\"")) offenders.Add("литеральная длительность анимации");

            Assert.True(offenders.Count == 0,
                "Словарь состояний пустоты вводит свои числа вместо токенов: " + string.Join(", ", offenders));
        }

        [Fact]
        public void EmptyStateDictionary_IsMergedAfterButtonStyles()
        {
            string appXaml = Read("App.xaml");
            string bootstrap = File.ReadAllText(Path.Combine(
                RepoRoot(), "MosquitoNetCalculator.Tests", "Helpers", "TestAppThemes.cs"));

            // EmptyState.Action наследует GhostButton через BasedOn StaticResource:
            // если словарь окажется раньше ButtonStyles, стиль не разрешится.
            int buttonStyles = appXaml.IndexOf("Themes/ButtonStyles.xaml", StringComparison.Ordinal);
            int emptyStates = appXaml.IndexOf("Themes/EmptyStateStyles.xaml", StringComparison.Ordinal);

            Assert.True(emptyStates > 0, "App.xaml не мержит Themes/EmptyStateStyles.xaml.");
            Assert.True(buttonStyles >= 0 && emptyStates > buttonStyles,
                "Themes/EmptyStateStyles.xaml должен мержиться после ButtonStyles (BasedOn GhostButton).");
            Assert.Contains("EmptyStateStyles.xaml", bootstrap);
        }

        [Fact]
        public void EmptyStateGlyphs_AreOnTheTypeScale()
        {
            string text = Read("Themes/EmptyStateStyles.xaml");

            // Крупный глиф — «здесь пусто целиком», компактный — «не найдено
            // внутри списка». Оба размера из шкалы, а не из воздуха.
            Assert.Contains("{DynamicResource Type.Hero}", text);
            Assert.Contains("{DynamicResource Type.Display}", text);
            Assert.Contains("{DynamicResource Font.Icon}", text);
        }
    }
}
