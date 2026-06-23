using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.App
{
    /// <summary>
    /// Smoke-test suite that mirrors TESTING_CHECKLIST.md — one test per
    /// acceptance bullet so a regression can be tagged with the exact
    /// section number that broke.
    ///
    /// Existing test files already cover deeper topics:
    ///  • UndoRedoServiceTests        → §4.1–4.4 (undo stack maths)
    ///  • OrdersHistoryViewModelTests → §5/§8 round-trips (deeper cases)
    ///  • PrintServiceTests           → §7 (deeper template text checks)
    ///  • AppLifecycleTests           → static pin tests for shadows/KP
    ///
    /// This file targets the gaps — each test method below maps to a
    /// SPECIFIC checklist bullet so a regression names the source line
    /// of the bug at the same time.
    ///
    /// File-IO isolation: AppSettingsService.SettingsPath,
    /// PriceService.PricesPath and OrderStorageService.OrdersDir are
    /// exposed as `public static string { get; set; }` on the
    /// production types (NOT `readonly` — .NET 8 throws
    /// `FieldAccessException` on `FieldInfo.SetValue` against
    /// `initonly` fields). We redirect them to a unique temp directory
    /// per test instance (xUnit creates a fresh test class instance
    /// per [Fact]) and restore the original values on Dispose so
    /// subsequent test classes aren't broken.
    /// </summary>
    [Collection("FileSystem")]
    public class ManualChecklistTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalSettingsPath;
        private readonly string _originalPricesPath;
        private readonly string _originalOrdersDir;

        public ManualChecklistTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mosquito_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            Directory.CreateDirectory(Path.Combine(_tempDir, "orders"));

            // Snapshot the production paths so Dispose can restore them
            // verbatim. Note: any test that mutates a path AFTER this
            // snapshot won't be reverted by Dispose — keep mutations in
            // the constructor only.
            _originalSettingsPath = AppSettingsService.SettingsPath;
            _originalPricesPath = PriceService.PricesPath;
            _originalOrdersDir = OrderStorageService.OrdersDir;

            AppSettingsService.SettingsPath = Path.Combine(_tempDir, "settings.json");
            PriceService.PricesPath = Path.Combine(_tempDir, "prices.json");
            OrderStorageService.OrdersDir = Path.Combine(_tempDir, "orders");
        }

        public void Dispose()
        {
            AppSettingsService.SettingsPath = _originalSettingsPath;
            PriceService.PricesPath = _originalPricesPath;
            OrderStorageService.OrdersDir = _originalOrdersDir;

            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup; user tempdir garbage is acceptable */ }
        }

        // ═══════════════════════════════════════════════════════════════════
        // §1. Переключение темы
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void Check1_Animation_Duration_Is_280ms()
        {
            // §1.2 — "Все цвета плавно переходят (анимация ~280мс)".
            // TransitionDuration is the single source of truth for the
            // theme-tween animation; this pin catches any accidental
            // regression that drops or extends it without semantic intent.
            Assert.Equal(TimeSpan.FromMilliseconds(280), ThemeService.TransitionDuration);
        }

        [Fact]
        public void Check1_ToggleTheme_Fires_ThemeChanged_And_Flips_State()
        {
            // §1.1 + §1.4 — pressing the theme button must publish
            // ThemeChanged AND flip IsDarkTheme in the same call.
            // We don't bring up Application.Current here on purpose:
            // ThemeService short-circuits ApplyTheme() when there's no
            // Application, so ToggleTheme stays safe on any thread —
            // which keeps this test a pure unit (no STA overhead).
            int fireCount = 0;
            void Handler() => fireCount++;
            ThemeService.ThemeChanged += Handler;
            try
            {
                bool wasDark = ThemeService.IsDarkTheme;
                ThemeService.ToggleTheme();
                Assert.True(fireCount >= 1);
                Assert.NotEqual(wasDark, ThemeService.IsDarkTheme);
            }
            finally
            {
                ThemeService.ThemeChanged -= Handler;
                ThemeService.ToggleTheme(); // restore to original state
            }
        }

        [Fact]
        public void Check1_Theme_Persists_Via_AppSettingsService()
        {
            // §1.5 — "Закройте приложение → откройте заново → тема сохранилась".
            // LoadTheme is re-read from settings.json on every call (no
            // in-memory cache), so Save → Load round-trips and validates
            // that the value survives process restart semantics. The
            // SettingsPath was redirected to a temp dir, so the user's
            // real settings.json is safe; the temp file is reaped by the
            // shared Dispose() at the end of this test instance.
            AppSettingsService.SaveTheme("dark");
            Assert.Equal("dark", AppSettingsService.LoadTheme());
            AppSettingsService.SaveTheme("light");
            Assert.Equal("light", AppSettingsService.LoadTheme());
        }

        // ═══════════════════════════════════════════════════════════════════
        // §2. Расчёт позиций — справочные цифры из чеклиста
        // ═══════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData("Anwis", "Белый", 1000, 1000, 1, 1800, 0.972, 1749.60)]     // ББ60: calcW=1002, H=970 → 0.972 м² × 1800
        [InlineData("ПСУЛ", "", 1000, 2000, 1, 100, 6.000, 600.00)]                // 6 м.п. × 100
        [InlineData("Уплотнение", "Серый", 1000, 2000, 1, 250, 6.000, 1500.00)]    // 6 м.п. × 250
        [InlineData("Работа", "", 0, 0, 1, 5000, 1.0, 5000.00)]                    // 1 шт. × 5000
        public void Check2_Single_Position_Calculates_Formula(
            string name, string color, int w, int h, int qty, double price,
            double expectedCalculated, double expectedItemTotal)
        {
            // §2.1–§2.4 — формульные цифры из чеклиста.
            var calc = new CalculationViewModel();
            calc.AddItem(name, color, w, h, qty, price);
            var item = calc.OrderItems.Single();
            Assert.Equal(expectedCalculated, item.CalculatedValue, 3);
            Assert.Equal(expectedItemTotal, item.Total, 2);
        }

        [Fact]
        public void Check2_Aggregate_Total_Is_Sum_Of_All_Active_Rows()
        {
            // §2.5 — "Итоговая сумма внизу = сумма всех активных строк".
            // 1749.6 + 600 + 1500 + 5000 = 8849.60 ₽.
            var calc = new CalculationViewModel();
            calc.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            calc.AddItem("ПСУЛ", "", 1000, 2000, 1, 100);
            calc.AddItem("Уплотнение", "Серый", 1000, 2000, 1, 250);
            calc.AddItem("Работа", "", 0, 0, 1, 5000);
            var total = calc.CalculateTotal(additionalKpTotal: 0);
            // Anwis ББ60 default: 0.972 м² × 1800 = 1749.6. Total: 1749.6+600+1500+5000 = 8849.6.
            Assert.Equal(8849.60, total.Total, 2);
            // Cast to int so we don't trigger xUnit2013 (Assert.Equal on .Count)
            int count = total.Count;
            Assert.Equal(4, count);
        }

        // ═══════════════════════════════════════════════════════════════════
        // §3. Переключатель монтажа
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void Check3_Mode0_Included_TotalUnchanged()
        {
            var item = new OrderItem
            {
                Name = "Anwis", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, InstallationMode = 0
            };
            Assert.Equal(1800.00, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void Check3_Mode1_Bez_Montazha_Deducts_Default500()
        {
            // §3.1 — case ✕ (без монтажа): сумма −500.
            var item = new OrderItem
            {
                Name = "Anwis", Width = 1000, Height = 1000, Quantity = 1, Price = 1800
            };
            item.InstallationMode = 1;
            Assert.Equal(1300.00, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void Check3_Mode2_V_Konstruktsiyu_Deducts_Default500()
        {
            // §3.1 + §3.2 — case «В конструкцию» ДОЛЖЕН вычитать 500 ₽
            // (v3.21.0 fixed this — used to add 0). Header label must be
            // «В конструкцию» (without the duplicate «В В» typo).
            var item = new OrderItem
            {
                Name = "Anwis", Width = 1000, Height = 1000, Quantity = 1, Price = 1800
            };
            item.InstallationMode = 2;
            Assert.Equal(1300.00, item.TotalWithDeduction, 2);
            Assert.Equal("В конструкцию", item.InstallationLabel);
            Assert.Equal("В", item.KpInstallationDisplay); // печатный глиф «В»
        }

        [Fact]
        public void Check3_Applies_Only_To_Anwis_And_Navesi()
        {
            // §3.3 + §3.4.
            Assert.True(new OrderItem { Name = "Anwis" }.IsInstallationApplicable);
            Assert.True(new OrderItem { Name = "На навесах" }.IsInstallationApplicable);
            // Отлив и ПСУЛ — переключатель неактивен.
            Assert.False(new OrderItem { Name = "Отлив" }.IsInstallationApplicable);
            Assert.False(new OrderItem { Name = "ПСУЛ" }.IsInstallationApplicable);
        }

        [Fact]
        public void Check3_NonApplicable_Renders_Dash()
        {
            // §3.4 — серый «—» в колонке.
            var item = new OrderItem { Name = "Отлив", Width = 200, Height = 1000, Quantity = 1, Price = 2150 };
            Assert.Equal("—", item.InstallationDisplay);
            Assert.Equal("—", item.KpInstallationDisplay);
        }

        [Fact]
        public void Check3_Custom_Amount_Overrides_Default_Deduction()
        {
            // §3.5 — пользовательская сумма в поле «Сумма:».
            var item = new OrderItem
            {
                Name = "Anwis", Width = 1000, Height = 1000, Quantity = 1, Price = 1800
            };
            item.InstallationMode = 1;
            item.SetCurrentInstallationAmount(1000);
            Assert.Equal(800.00, item.TotalWithDeduction, 2);   // 1800 − 1000

            item.InstallationMode = 2;
            item.SetCurrentInstallationAmount(250);
            Assert.Equal(1550.00, item.TotalWithDeduction, 2);  // 1800 − 250
        }

        // ═══════════════════════════════════════════════════════════════════
        // §4. Отмена/Повтор — интеграция с Dirty-индикатором
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void Check4_DirtyCallback_Fires_When_MarkDirty_Or_MarkClean()
        {
            // §4.5 — «Индикатор «● Есть изменения» появляется
            // при редактировании и исчезает после сохранения».
            // SetDirtyCallback is the wire MainWindow subscribes to
            // (see MainWindow ctor: SetDirtyCallback(UpdateDirtyIndicator));
            // this test asserts the underlying signal works as expected.
            //
            // UndoRedoService is a static singleton, so neighbouring test
            // classes may leave it in IsDirty=true state. MarkDirty's
            // body short-circuits when _isDirty is already true and never
            // fires the callback, so we explicitly reset to clean BEFORE
            // we wire our own handler.
            var undo = UndoRedoService.Instance;
            undo.MarkClean();

            int fires = 0;
            undo.SetDirtyCallback(() => fires++);
            int baseline = fires;

            undo.MarkDirty();
            Assert.True(undo.IsDirty);
            Assert.True(fires > baseline,
                "SetDirtyCallback should fire on MarkDirty");
            int afterDirty = fires;

            undo.MarkClean();
            Assert.False(undo.IsDirty);
            Assert.True(fires > afterDirty,
                "SetDirtyCallback should fire on MarkClean");
        }

        // ═══════════════════════════════════════════════════════════════════
        // §5. Сохранение и загрузка заказов + контракт-номер на смене префикса
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void Check5_Save_And_Load_Roundtrips_Data()
        {
            var storage = new OrderStorageService();
            var order = new OrderData
            {
                Id = Guid.NewGuid().ToString(),
                ContractNumber = "1-1",
                ClientName = "Тестовый Заказчик",
                ClientPhone = "+7 999 123 45 67",
                ClientAddress = "ул. Тестовая, 1",
                TotalAmount = 5000.00,
                ContractDate = new DateTime(2026, 6, 17)
            };
            storage.SaveOrder(order);
            var loaded = storage.LoadOrder(order.Id);
            Assert.NotNull(loaded);
            Assert.Equal("Тестовый Заказчик", loaded!.ClientName);
            Assert.Equal(5000.00, loaded.TotalAmount, 2);
        }

        [Fact]
        public void Check5_Delete_Removes_From_List()
        {
            var vm = new OrdersHistoryViewModel();
            var order = new OrderData { Id = Guid.NewGuid().ToString(), ContractNumber = "1-1" };
            vm.SaveOrder(order);
            Assert.Single(vm.LoadAllOrders());
            vm.DeleteOrder(order.Id);
            Assert.Empty(vm.LoadAllOrders());
        }

        [Fact]
        public void Check5_Contract_Number_Increments_Per_Prefix()
        {
            // §5 + §6 — explicit user-visible acceptance test:
            // "корректный контракт-номер при смене префикса".
            // Save orders under prefix "1" (1-1, 1-2 → next is 1-3),
            // then ask for prefix "2" (independent counter → 2-1),
            // save a "2-1" then ask again (2-2), and finally check
            // the "1" prefix is unaffected.
            var storage = new OrderStorageService();
            storage.SaveOrder(new OrderData { Id = Guid.NewGuid().ToString(), ContractNumber = "1-1" });
            storage.SaveOrder(new OrderData { Id = Guid.NewGuid().ToString(), ContractNumber = "1-2" });
            Assert.Equal("1-3", storage.GenerateContractNumber("1"));
            Assert.Equal("2-1", storage.GenerateContractNumber("2"));
            storage.SaveOrder(new OrderData { Id = Guid.NewGuid().ToString(), ContractNumber = "2-1" });
            Assert.Equal("2-2", storage.GenerateContractNumber("2"));
            // Reverse — going back to prefix 1 should still report 1-3.
            Assert.Equal("1-3", storage.GenerateContractNumber("1"));
        }

        // ═══════════════════════════════════════════════════════════════════
        // §6. Дополнительные КП
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void Check6_Enable_AdditionalKps_Adds_Item_And_Updates_Total()
        {
            // §6.1 + §6.2 — галочка показывает поле ввода, сумма прибавляется.
            var c = new ClientInfo();
            c.HasAdditionalKp = true;
            c.AdditionalKps[0].Number = "2-1";
            c.AdditionalKps[0].Amount = 500;
            Assert.Equal(500.0, c.AdditionalKpsTotal);

            var calc = new CalculationViewModel();
            var total = calc.CalculateTotal(c.AdditionalKpsTotal);
            Assert.Equal(500.0, total.Total, 2);
        }

        [Fact]
        public void Check6_Uncheck_Hides_Block_But_Preserves_Number_And_Amount()
        {
            // §6.3 + §6.4 — данные не теряются при снятии/установке галочки.
            var c = new ClientInfo();
            c.HasAdditionalKp = true;
            c.AdditionalKps[0].Number = "2-1";
            c.AdditionalKps[0].Amount = 500;
            Assert.Single(c.AdditionalKps);

            c.HasAdditionalKp = false;
            Assert.Equal(0.0, c.AdditionalKpsTotal);  // вклад в ИТОГО обнуляется
            Assert.Single(c.AdditionalKps);           // данные сохранены

            c.HasAdditionalKp = true;
            Assert.Equal(500.0, c.AdditionalKpsTotal);
            Assert.Equal("2-1", c.AdditionalKps[0].Number);
            Assert.Equal(500.0, c.AdditionalKps[0].Amount);
        }

        [Fact]
        public void Check6_Multiple_AdditionalKps_Sum_Correctly()
        {
            // §6.5.
            var c = new ClientInfo();
            c.HasAdditionalKp = true;
            c.AdditionalKps[0].Number = "2-1";
            c.AdditionalKps[0].Amount = 500;
            c.AdditionalKps.Add(new AdditionalKpItem { Number = "2-2", Amount = 750 });
            Assert.Equal(1250.0, c.AdditionalKpsTotal);
        }

        // ═══════════════════════════════════════════════════════════════════
        // §7. Печать КП — структурные проверки HTML
        // ═══════════════════════════════════════════════════════════════════

        private static List<OrderItem> SampleItems() => new()
        {
            new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800 }
        };

        private static ClientInfo SampleClient() => new()
        {
            ClientName = "Иванов",
            ClientPhone = "+7 999 000 00 00",
            ClientAddress = "ул. Ленина, 15",
            ContractNumber = "1-1",
            ContractDate = new DateTime(2026, 6, 17)
        };

        [Fact]
        public void Check7_Print_Includes_ClientName_Phone_Address_ContractNumber_Date()
        {
            var ps = new PrintService();
            var html = ps.GenerateKpHtml(SampleItems(), SampleClient(), 1800, "Одна тысяча восемьсот рублей");
            Assert.Contains("Иванов", html);
            Assert.Contains("+7 999 000 00 00", html);
            Assert.Contains("ул. Ленина, 15", html);
            Assert.Contains("1-1", html);
            Assert.Contains("17.06.2026", html);
        }

        [Fact]
        public void Check7_Print_Includes_AmountInWords()
        {
            // §7.4 — итоговая сумма прописью.
            var ps = new PrintService();
            var html = ps.GenerateKpHtml(SampleItems(), SampleClient(), 1800, "Одна тысяча восемьсот рублей");
            Assert.Contains("Одна тысяча восемьсот рублей", html);
        }

        [Fact]
        public void Check7_Print_Renders_Table_Row_For_Each_Item()
        {
            // §7.3 — все позиции в таблице с размерами. Pin the table
            // cells (name-cell class) so we catch a regression that drops
            // items from the КП even when other table elements survive.
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800 },
                new() { Name = "ПСУЛ", Width = 1000, Height = 2000, Quantity = 1, Price = 100 }
            };
            var ps = new PrintService();
            var html = ps.GenerateKpHtml(items, SampleClient(), 2400, "Две тысячи четыреста рублей");
            Assert.Contains("<td class='name-cell'>Anwis</td>", html);
            Assert.Contains("<td class='name-cell'>ПСУЛ</td>", html);
            // Размеры выводятся в колонках Ш и В.
            Assert.Contains("<td class='center'>1000</td>", html);
        }

        [Fact]
        public void Check7_Print_Renders_AdditionalKp_Block_When_Present()
        {
            // §7.5.
            var c = SampleClient();
            c.HasAdditionalKp = true;
            c.AdditionalKps[0].Number = "2-1";
            c.AdditionalKps[0].Amount = 500;
            var ps = new PrintService();
            var html = ps.GenerateKpHtml(SampleItems(), c, 1800, "Одна тысяча восемьсот рублей");
            Assert.Contains("2-1", html);
            Assert.Contains("500,00", html);
            // Заголовок блока ДОПОЛНИТЕЛЬНОЕ КП (рус. uppercase).
            Assert.Contains("ДОПОЛНИТЕЛЬНОЕ", html.ToUpper());
        }

        [Fact]
        public void Check7_Print_Renders_V_Konstr_Glyph_For_Mode2()
        {
            // §7.6 — для «Anwis» в режиме «В» показывается «В констр.» в таблице печати.
            // В таблице есть <span class='install-mark' title='...'>В</span>.
            // PrintService uses single quotes for HTML attributes throughout the
            // template, so the title attribute uses title='...' not title="...".
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, InstallationMode = 2 }
            };
            var ps = new PrintService();
            var html = ps.GenerateKpHtml(items, SampleClient(), 1300, "Одна тысяча триста рублей");
            Assert.Contains("title='В конструкцию'", html);
            Assert.Contains(">В</span>", html);
        }

        // ═══════════════════════════════════════════════════════════════════
        // §8. Экспорт / Импорт заказов
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void Check8_Export_Roundtrip_And_Duplicate_Filter()
        {
            // §8.1 + §8.2 + §8.3 — экспорт, импорт обратно, повторный импорт = ноль новых.
            // Order in the JSON file is dictated by LoadAllOrders's UpdatedAt-desc
            // sort — an implementation detail we don't want to encode into the test.
            // Use order-agnostic assertions (Contains) so the test survives
            // any future sort-order change.
            var vm = new OrdersHistoryViewModel();
            var a = new OrderData { Id = Guid.NewGuid().ToString(), ContractNumber = "1-1", ClientName = "A" };
            var b = new OrderData { Id = Guid.NewGuid().ToString(), ContractNumber = "1-2", ClientName = "B" };
            vm.SaveOrder(a);
            vm.SaveOrder(b);

            var exportPath = Path.Combine(_tempDir, "export.json");
            vm.ExportOrders(vm.LoadAllOrders(), exportPath);
            Assert.True(File.Exists(exportPath));
            var json = File.ReadAllText(exportPath);
            Assert.Contains("\"ClientName\": \"A\"", json);
            Assert.Contains("\"ClientName\": \"B\"", json);

            var read = vm.ReadOrdersFromFile(exportPath);
            Assert.NotNull(read);
            Assert.Contains(read!, o => o.ClientName == "A");
            Assert.Contains(read!, o => o.ClientName == "B");

            var imported = vm.MergeImport(read!);
            Assert.Empty(imported); // дубликаты не создаются
            var afterMerge = vm.LoadAllOrders();
            Assert.Contains(afterMerge, o => o.ContractNumber == "1-1");
            Assert.Contains(afterMerge, o => o.ContractNumber == "1-2");
        }

        [Fact]
        public void Check8_Export_Single_Order_File_Contains_Only_One_Row()
        {
            // §8.4 — экспорт отдельного заказа содержит только его.
            var vm = new OrdersHistoryViewModel();
            var a = new OrderData { Id = Guid.NewGuid().ToString(), ContractNumber = "1-1" };
            var b = new OrderData { Id = Guid.NewGuid().ToString(), ContractNumber = "1-2" };
            vm.SaveOrder(a);
            vm.SaveOrder(b);
            var exportPath = Path.Combine(_tempDir, "single.json");
            vm.ExportOrders(new List<OrderData> { a }, exportPath);
            var read = vm.ReadOrdersFromFile(exportPath);
            Assert.NotNull(read);
            Assert.Single(read!);
            Assert.Equal(a.Id, read![0].Id);
        }

        // ═══════════════════════════════════════════════════════════════════
        // §9. Цены (вкладка «Цены»)
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void Check9_Default_Anwis_Beliye_Is_1800()
        {
            // §9.1 — список товаров с ценами при первом запуске.
            var ps = new PriceService();
            var prices = ps.LoadPrices(); // temp dir пуст → defaults создаются
            Assert.Equal(1800, ps.GetPrice(prices, "Anwis", "Белый"));
        }

        [Fact]
        public void Check9_Modified_Price_Persists_Across_Reload()
        {
            // §9.2 + §9.3 — изменение сохраняется после «рестарта».
            var ps1 = new PriceService();
            var prices = ps1.LoadPrices();
            ps1.SavePrices(prices); // гарантируем, что prices.json существует
            for (int i = 0; i < prices.Count; i++)
                if (prices[i].Name == "Anwis" && prices[i].Color == "Белый")
                    prices[i].Price = 2000;
            ps1.SavePrices(prices);

            // fresh instance reads disk again → 2000.
            var ps2 = new PriceService();
            var prices2 = ps2.LoadPrices();
            Assert.Equal(2000, ps2.GetPrice(prices2, "Anwis", "Белый"));
        }

        // ═══════════════════════════════════════════════════════════════════
        // §10. Журнал обновлений
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void Check10_Newest_First_Index_0_Is_Freshest()
        {
            // §10.1 — записи отсортированы по убыванию даты (свежие наверху).
            var updates = UpdateLog.AllNewestFirst();
            Assert.NotEmpty(updates);
            for (int i = 0; i < updates.Count - 1; i++)
                Assert.True(updates[i].Date >= updates[i + 1].Date,
                    $"Index {i} ({updates[i].Date:yyyy-MM-dd}) must be ≥ index {i + 1} ({updates[i + 1].Date:yyyy-MM-dd})");
        }

        [Fact]
        public void Check10_Every_Entry_Has_Version_Title_Type_And_Changes()
        {
            // §10.2 — бейджи типа отображаются, даты валидны.
            foreach (var u in UpdateLog.AllNewestFirst())
            {
                Assert.False(string.IsNullOrWhiteSpace(u.Version));
                Assert.False(string.IsNullOrWhiteSpace(u.Title));
                Assert.False(string.IsNullOrWhiteSpace(u.Type));
                Assert.NotEmpty(u.Changes);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // §11. Максимизация окна — приватный WndProc ⇒ статический пин
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void Check11_MainWindow_WndProc_Handles_WM_GETMINMAXINFO()
        {
            // §11 — v3.34.4: WndProc & Win32 interop moved to
            // MainWindow.WindowChrome.cs (partial class).
            // Check all partial files for the clamping-logic strings.
            var dir = LocateSourceProjectDir();
            var src = File.ReadAllText(Path.Combine(dir, "MainWindow.xaml.cs"))
                + File.ReadAllText(Path.Combine(dir, "MainWindow.WindowChrome.cs"));
            Assert.Contains("WM_GETMINMAXINFO", src);
            Assert.Contains("GetMonitorInfo", src);
            Assert.Contains("ptMaxPosition", src);
            Assert.Contains("ptMaxSize", src);
            Assert.Contains("rcWork", src);
        }

        private static string LocateSourceProjectDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator", "App.xaml")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "MosquitoNetCalculator");
        }

        // ═══════════════════════════════════════════════════════════════════
        // §12. «Отправить на завод»
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public void Check12_BuildSelectableItems_Production_On_NonProduction_Off()
        {
            // §12.2 — производственные товары включены, непроизводственные сняты.
            // BuildSelectableItems filters by `i.Total > 0` so every item
            // need a non-zero price (manual-piece products set CalculatedValue = 1).
            var order = new CalculationViewModel();
            order.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);    // ✓
            order.AddItem("На навесах", "Белый", 1000, 1000, 1, 2900); // ✓
            order.AddItem("ПСУЛ", "", 1000, 2000, 1, 100);            // ✗
            order.AddItem("Работа", "", 0, 0, 1, 5000);               // ✗
            order.AddItem("Доставка", "", 0, 0, 1, 500);              // ✗
            order.AddItem("Брус", "", 0, 0, 2, 100);                  // ✗ (price > 0!)
            order.AddItem("Пояс", "", 0, 0, 1, 150);                  // ✗ (price > 0!)

            var kps = new List<AdditionalKpItem>
            {
                new() { Number = "2-1", Amount = 500, IsActive = true }
            };

            var selectables = FactoryTextService.BuildSelectableItems(order.OrderItems, kps);
            // 7 order items + 1 KP. Use local int variables to bypass
            // xUnit2013 (Assert.Equal on collection .Count).
            int totalCount = selectables.Count;
            Assert.Equal(8, totalCount);
            int orderCount = selectables.Count(s => s.OrderItem != null);
            Assert.Equal(7, orderCount);
            int kpCount = selectables.Count(s => s.AdditionalKp != null);
            Assert.Equal(1, kpCount);
            Assert.True(selectables.Single(s => s.IsAdditionalKp).IsSelected);
            Assert.True(selectables.Single(s => s.OrderItem?.Name == "Anwis").IsSelected);
            Assert.True(selectables.Single(s => s.OrderItem?.Name == "На навесах").IsSelected);
            Assert.False(selectables.Single(s => s.OrderItem?.Name == "ПСУЛ").IsSelected);
            Assert.False(selectables.Single(s => s.OrderItem?.Name == "Работа").IsSelected);
            Assert.False(selectables.Single(s => s.OrderItem?.Name == "Доставка").IsSelected);
            Assert.False(selectables.Single(s => s.OrderItem?.Name == "Брус").IsSelected);
            Assert.False(selectables.Single(s => s.OrderItem?.Name == "Пояс").IsSelected);
            Assert.True(selectables.Single(s => s.IsAdditionalKp).IsSelected);
        }

        [Fact]
        public void Check12_Generate_Includes_Address_When_Set()
        {
            // §12.5 — «Адрес: ул. Ленина, 15» первой строкой.
            var items = SampleItems();
            var selectables = FactoryTextService.BuildSelectableItems(items, Enumerable.Empty<AdditionalKpItem>());
            selectables.ForEach(s => s.IsSelected = true);
            var text = FactoryTextService.Generate("ул. Ленина, 15", selectables);
            Assert.Contains("Адрес: ул. Ленина, 15", text);
        }

        [Fact]
        public void Check12_Generate_Omits_Address_When_Empty()
        {
            // §12.5 — если адрес не заполнен, строки «Адрес:» НЕТ.
            var items = SampleItems();
            var selectables = FactoryTextService.BuildSelectableItems(items, Enumerable.Empty<AdditionalKpItem>());
            selectables.ForEach(s => s.IsSelected = true);
            var text = FactoryTextService.Generate("", selectables);
            Assert.DoesNotContain("Адрес:", text);
        }

        [Fact]
        public void Check12_Generate_Groups_Items_By_Name()
        {
            // §12.5 — товары сгруппированы по названию с двоеточием.
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800 },
                new() { Name = "Anwis", Color = "Белый", Width = 1200, Height = 1400, Quantity = 2, Price = 1800 },
                new() { Name = "Отлив", Width = 200, Height = 1000, Quantity = 1, Price = 2150 }
            };
            var selectables = FactoryTextService.BuildSelectableItems(items, Enumerable.Empty<AdditionalKpItem>());
            selectables.ForEach(s => s.IsSelected = true);
            var text = FactoryTextService.Generate("", selectables);
            Assert.Contains("Отлив:", text);
            Assert.Contains("1 шт.", text); // Отлив 1 шт
            Assert.Contains("2 шт.", text); // Anwis second row
        }

        [Fact]
        public void Check12_Generate_Formats_Dimensions_For_Anwis_Default_Mode()
        {
            // §12.5 — stored calc for ББ60: 1002×970, copy = stored−20 = 982×950.
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1002, Height = 970, Quantity = 1, Price = 1800, AnwisSizeMode = AnwisSizeMode.Брусбокс60 }
            };
            var selectables = FactoryTextService.BuildSelectableItems(items, Enumerable.Empty<AdditionalKpItem>());
            selectables.ForEach(s => s.IsSelected = true);
            var text = FactoryTextService.Generate("", selectables);
            Assert.Contains("Anwis, размер проёма (ББ 60):", text);
        }

        [Fact]
        public void Check12_Generate_Manual_Piece_Items_Use_Qty_Only_Format()
        {
            // §12.5 — «Для штучных товаров (Работа, Доставка) формат: 1 шт.
            // (без размеров)».
            var items = new List<OrderItem>
            {
                new() { Name = "Работа", Quantity = 3, Price = 5000 }
            };
            var selectables = FactoryTextService.BuildSelectableItems(items, Enumerable.Empty<AdditionalKpItem>());
            // Работа по умолчанию снята — включим принудительно для теста.
            selectables.ForEach(s => s.IsSelected = true);
            var text = FactoryTextService.Generate("", selectables);
            Assert.Contains("Работа:", text);
            Assert.Contains("3 шт.", text);
            Assert.DoesNotContain("Ш:", text); // без размеров
        }

        [Fact]
        public void Check12_Generate_Includes_Active_AdditionalKps_Lines()
        {
            // §12.5 + §12.9 — строки «К КП № 2-1», «К КП № 2-2».
            var items = SampleItems();
            var kps = new List<AdditionalKpItem>
            {
                new() { Number = "2-1", Amount = 500, IsActive = true },
                new() { Number = "2-2", Amount = 750, IsActive = true }
            };
            var selectables = FactoryTextService.BuildSelectableItems(items, kps);
            selectables.ForEach(s => s.IsSelected = true);
            var text = FactoryTextService.Generate("адрес", selectables);
            Assert.Contains("К КП № 2-1", text);
            Assert.Contains("К КП № 2-2", text);
        }

        [Fact]
        public void Check12_Generate_Omits_Unselected_Items()
        {
            // §12.4 — «Снять все» → пустой предпросмотр.
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800 },
                new() { Name = "ПСУЛ", Width = 1000, Height = 2000, Quantity = 1, Price = 100 }
            };
            var selectables = FactoryTextService.BuildSelectableItems(items, Enumerable.Empty<AdditionalKpItem>());
            selectables.ForEach(s => s.IsSelected = false);
            var text = FactoryTextService.Generate("адрес", selectables);
            Assert.DoesNotContain("Anwis", text);
            Assert.DoesNotContain("ПСУЛ", text);
        }

        [Fact]
        public void Check12_Generate_Inactive_Kp_Excluded_Empty_Number_Fallback()
        {
            // §12.9 — неактивное Доп.КП не выводится; пустой номер → «К КП».
            var items = SampleItems();
            var kps = new List<AdditionalKpItem>
            {
                new() { Number = "", Amount = 500, IsActive = true },        // пустой номер → «К КП»
                new() { Number = "3-1", Amount = 250, IsActive = false }      // неактивное → игнор
            };
            var selectables = FactoryTextService.BuildSelectableItems(items, kps);
            var text = FactoryTextService.Generate("", selectables);
            Assert.Contains("К КП", text);          // fallback для пустого номера
            Assert.DoesNotContain("3-1", text);     // неактивное исключено
        }

        [Fact]
        public void Check12_Generate_Selectable_Count_Text_Shape()
        {
            // §12.6 — счётчики строк и символов считаются корректно:
            // пустой предпросмотр отдельно. Проверяем форму текста.
            var items = SampleItems();
            var selectables = FactoryTextService.BuildSelectableItems(items, Enumerable.Empty<AdditionalKpItem>());
            selectables.ForEach(s => s.IsSelected = false);
            var text = FactoryTextService.Generate("", selectables);
            // Пустой текст → нулевая длина (TrimEnd ещё убирает завершающие пробелы).
            Assert.Equal("", text);
        }
    }
}
