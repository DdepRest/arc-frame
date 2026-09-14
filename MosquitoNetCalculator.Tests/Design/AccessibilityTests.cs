using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Доступность интерфейса (v3.53.0).
    ///
    /// До этой версии UIA-имена были только у четырёх файлов из 24 контролов и
    /// 12 окон, а у иконочных кнопок (крестики, свернуть/развернуть, шестерёнка,
    /// форматирование заметок) не было вообще: экранный диктор читал «кнопка»,
    /// а UIA-харнесс мог найти элемент только по координатам.
    ///
    /// Покрытие растёт «ратчетом вверх»: floors ниже — обязательный минимум,
    /// он поднимается вместе с каждой правкой разметки.
    /// </summary>
    public class AccessibilityTests
    {
        /// <summary>Минимум элементов с AutomationProperties.Name на файл.</summary>
        private static readonly Dictionary<string, int> Floors = new(StringComparer.Ordinal)
        {
            ["Controls/TitleBarControl.xaml"] = 4,        // шестерёнка + свернуть/развернуть/закрыть
            ["Controls/SidebarControl.xaml"] = 5,         // форматирование заметок
            ["Controls/ActionBarControl.xaml"] = 1,
            ["Controls/AdminPasswordWindow.xaml"] = 1,
            ["Controls/AiApiKeyDialog.xaml"] = 1,
            ["Controls/AiAssistantControl.xaml"] = 20,    // уже был размечен
            ["Themes/ButtonStyles.xaml"] = 1,             // DialogCloseButton: одна точка на ~10 диалогов
        };

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

        private static int CountNames(string relativePath) =>
            Regex.Matches(File.ReadAllText(Path.Combine(AppDir, relativePath)), "AutomationProperties\\.Name").Count;

        [Fact]
        public void IconOnlyControls_HaveAccessibleNames()
        {
            var missing = Floors
                .Where(kv => CountNames(kv.Key) < kv.Value)
                .Select(kv => $"{kv.Key}: {CountNames(kv.Key)} < {kv.Value}")
                .ToList();

            Assert.True(missing.Count == 0,
                "У иконочных/интерактивных элементов нет имени для диктора и UIA:\n  " + string.Join("\n  ", missing));
        }

        [Fact]
        public void WindowControls_ExposeNames()
        {
            string titleBar = File.ReadAllText(Path.Combine(AppDir, "Controls", "TitleBarControl.xaml"));

            Assert.Contains("AutomationProperties.Name=\"Свернуть\"", titleBar);
            Assert.Contains("AutomationProperties.Name=\"Развернуть\"", titleBar);
            Assert.Contains("AutomationProperties.Name=\"Закрыть\"", titleBar);
            Assert.Contains("AutomationProperties.Name=\"Настройки и обновления\"", titleBar);

            // Подсказка описывает, что внутри меню — диктор читает её после имени.
            Assert.Contains("AutomationProperties.HelpText", titleBar);
        }

        [Fact]
        public void SharedCloseButton_CarriesNameForEveryDialog()
        {
            string buttonStyles = File.ReadAllText(Path.Combine(AppDir, "Themes", "ButtonStyles.xaml"));

            int styleStart = buttonStyles.IndexOf("x:Key=\"DialogCloseButton\"", StringComparison.Ordinal);
            Assert.True(styleStart >= 0, "Стиль DialogCloseButton не найден.");

            string style = buttonStyles[styleStart..];
            style = style[..style.IndexOf("</Style>", StringComparison.Ordinal)];

            // Один сеттер в общем стиле закрывает доступность ~10 диалогов —
            // именно поэтому он здесь, а не в каждом окне по отдельности.
            Assert.Contains("AutomationProperties.Name", style);
        }

        [Fact]
        public void NotesFormattingButtons_AreNamed_NotJustLettered()
        {
            string sidebar = File.ReadAllText(Path.Combine(AppDir, "Controls", "SidebarControl.xaml"));

            // «Ж»/«К»/«A»/«✕» сами по себе не читаются: нужен смысл.
            foreach (var name in new[] { "Жирный", "Курсив", "Цвет текста", "Список", "Очистить форматирование" })
            {
                Assert.True(sidebar.Contains($"AutomationProperties.Name=\"{name}\"", StringComparison.Ordinal),
                    $"Кнопка с подписью «{name}» не имеет AutomationProperties.Name.");
            }
        }
    }
}
