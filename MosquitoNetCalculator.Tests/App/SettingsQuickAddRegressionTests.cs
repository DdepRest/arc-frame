using System;
using System.IO;
using Xunit;

namespace MosquitoNetCalculator.Tests.App
{
    /// <summary>
    /// Текстовые регрессии на UX-правки 2026-08-23:
    /// 1) десятичная точка (0.65) автозаменяется на запятую в полях QuickAdd;
    /// 2) Enter в «Кол-во»/«Сумма» добавляет товар без клика по кнопке;
    /// 3) админ-панель переехала в меню настроек (шестерёнка), вход по паролю;
    /// 4) настройки AI (API-ключ) защищены админ-паролем.
    /// </summary>
    public sealed class SettingsQuickAddRegressionTests
    {
        private static string LocateSource(string relative)
        {
            var root = LocateProjectRoot();
            var path = Path.Combine(root, "MosquitoNetCalculator", relative);
            Assert.True(File.Exists(path), $"Source file not found: {path}");
            return path;
        }

        private static string LocateProjectRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        // ── 1. Автозамена точки на запятую ──────────────────────────────

        [Fact]
        public void QuickAdd_DecimalDot_IsAutoReplacedWithComma()
        {
            var code = File.ReadAllText(LocateSource("Controls/QuickAddControl.xaml.cs"));
            // 0.65 → 0,65: автозамена в TextChanged + робастный парсер с Replace('.', ',').
            Assert.Contains("tb.Text.Replace('.', ',')", code);
            Assert.Contains("TryParseQuickNumber", code);
            Assert.Contains(".Replace('.', ',')", code);
        }

        [Fact]
        public void QuickAdd_NumericFields_UseRobustParsing()
        {
            var addItem = File.ReadAllText(LocateSource("Controls/QuickAddControl.AddItem.cs"));
            var preview = File.ReadAllText(LocateSource("Controls/QuickAddControl.Preview.cs"));
            // Ни одно поле не парсится сырым int/double.TryParse — только робастный парсер.
            Assert.DoesNotContain("int.TryParse(TxtQuickWidth.Text", addItem);
            Assert.DoesNotContain("int.TryParse(TxtQuickHeight.Text", addItem);
            Assert.DoesNotContain("int.TryParse(TxtQuickQty.Text", addItem);
            Assert.DoesNotContain("double.TryParse(TxtQuickPrice.Text", addItem);
            Assert.DoesNotContain("int.TryParse(TxtQuickWidth.Text", preview);
            Assert.DoesNotContain("double.TryParse(TxtQuickPrice.Text", preview);
        }

        // ── 2. Enter в «Кол-во»/«Сумма» добавляет товар ─────────────────

        [Fact]
        public void QuickAdd_QtyAndPriceFields_SubmitOnEnter()
        {
            var xaml = File.ReadAllText(LocateSource("Controls/QuickAddControl.xaml"));
            // TxtQuickQty и TxtQuickPrice должны иметь KeyDown="QuickField_KeyDown"
            // (обработчик вызывает QuickAddItem на Enter).
            AssertHasKey(xaml, "TxtQuickQty");
            AssertHasKey(xaml, "TxtQuickPrice");
        }

        private static void AssertHasKey(string xaml, string name)
        {
            int start = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
            Assert.True(start >= 0, $"Поле {name} не найдено в QuickAddControl.xaml");
            int end = xaml.IndexOf("/>", start, StringComparison.Ordinal);
            Assert.True(end > start, $"Элемент {name} не закрыт");
            var snippet = xaml.Substring(start, end - start);
            Assert.Contains("KeyDown=\"QuickField_KeyDown\"", snippet);
        }

        [Fact]
        public void QuickField_KeyDown_AddsItemOnEnter()
        {
            var code = File.ReadAllText(LocateSource("Controls/QuickAddControl.xaml.cs"));
            Assert.Contains("private void QuickField_KeyDown(object sender, KeyEventArgs e)", code);
            Assert.Contains("if (e.Key == Key.Enter) QuickAddItem();", code);
        }

        // ── 3. Админ-панель в настройках ────────────────────────────────

        [Fact]
        public void AdminPanel_MovedOutOfSidebar_IntoSettingsMenu()
        {
            var mainXaml = File.ReadAllText(LocateSource("MainWindow.xaml"));
            var titleXaml = File.ReadAllText(LocateSource("Controls/TitleBarControl.xaml"));
            var titleCs = File.ReadAllText(LocateSource("Controls/TitleBarControl.xaml.cs"));

            // Кнопка «Админ-панель» убрана из левой навигации.
            Assert.DoesNotContain("NavBtnAdmin", mainXaml);
            // Пункт «Админ-панель…» появился в меню настроек (шестерёнка).
            Assert.Contains("Админ-панель", titleXaml);
            Assert.Contains("MenuAdminPanel_Click", titleXaml);
            // По открытию — запрос пароля + показ оверлея главного окна.
            Assert.Contains("PromptAdminPassword()", titleCs);
            Assert.Contains("ShowAdminPanel()", titleCs);
        }

        [Fact]
        public void NavigationArray_HasNoAdminButton()
        {
            var code = File.ReadAllText(LocateSource("MainWindow.xaml.cs"));
            Assert.DoesNotContain("NavBtnAdmin,", code);
            Assert.DoesNotContain("NavIconAdmin,", code);
            Assert.DoesNotContain("NavLabelAdmin,", code);
        }

        // ── 4. AI-настройки под паролем ─────────────────────────────────

        [Fact]
        public void AiApiKeySettings_RequireAdminPassword()
        {
            var titleCs = File.ReadAllText(LocateSource("Controls/TitleBarControl.xaml.cs"));
            // Пароль запрашивается ДО открытия диалога AI-настроек.
            var aiMethod = titleCs.IndexOf("MenuAiApiKey_Click", StringComparison.Ordinal);
            var adminMethod = titleCs.IndexOf("PromptAdminPassword", StringComparison.Ordinal);
            Assert.True(aiMethod >= 0, "MenuAiApiKey_Click должен существовать");
            // PromptAdminPassword определён выше и вызывается внутри MenuAiApiKey_Click.
            var body = titleCs.Substring(aiMethod);
            Assert.Contains("PromptAdminPassword()", body);
        }
    }
}