using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MosquitoNetCalculator.Controls;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Unit tests for <see cref="QuickAddControl.UpdateAnticatToggleState"/>.
    /// Verifies that the anti-cat toggle button visibility and checked state
    /// update correctly when the product type changes.
    ///
    /// These tests cover the extracted helper in isolation. Full integration
    /// of <see cref="QuickAddControl.CmbQuickType_SelectionChanged"/> is
    /// blocked by the <c>TryGetMainWindow → MainWindow</c> dependency.
    ///
    /// WPF <see cref="ToggleButton"/> requires an STA thread. xUnit defaults to
    /// MTA, so every test body is dispatched to a dedicated STA thread via
    /// <see cref="RunOnStaThread"/>.
    /// </summary>
    public class QuickAddControlTests
    {
        /// <summary>
        /// Runs <paramref name="action"/> on a dedicated STA thread,
        /// re-throwing any exception on the calling thread. Times out
        /// after 10 seconds.
        /// </summary>
        private static void RunOnStaThread(Action action)
        {
            Exception? caught = null;
            using var gate = new ManualResetEventSlim(false);

            var t = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex) { caught = ex; }
                finally { gate.Set(); }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();

            if (!gate.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("STA thread did not finish within 10 seconds.");

            if (caught != null)
                throw caught;
        }

        [Theory]
        [InlineData("Anwis")]
        [InlineData("На навесах")]
        [InlineData("Оконная на метал. крепл.")]
        public void UpdateAnticatToggleState_ApplicableProduct_ShowsButton(string productName)
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton();
                QuickAddControl.UpdateAnticatToggleState(productName, btn);

                Assert.Equal(Visibility.Visible, btn.Visibility);
            });
        }

        [Theory]
        [InlineData("Отлив")]
        [InlineData("Козырёк")]
        [InlineData("Работа")]
        [InlineData("ПСУЛ")]
        [InlineData("Доставка")]
        public void UpdateAnticatToggleState_NonApplicableProduct_HidesButton(string productName)
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton();
                QuickAddControl.UpdateAnticatToggleState(productName, btn);

                Assert.Equal(Visibility.Collapsed, btn.Visibility);
            });
        }

        [Fact]
        public void UpdateAnticatToggleState_NonApplicableProduct_UnchecksButton()
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton { IsChecked = true };
                QuickAddControl.UpdateAnticatToggleState("Отлив", btn);

                Assert.False(btn.IsChecked);
            });
        }

        [Fact]
        public void UpdateAnticatToggleState_ApplicableProduct_PreservesCheckedState()
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton { IsChecked = true };
                QuickAddControl.UpdateAnticatToggleState("Anwis", btn);

                Assert.Equal(Visibility.Visible, btn.Visibility);
                Assert.True(btn.IsChecked);
            });
        }

        [Fact]
        public void UpdateAnticatToggleState_SwitchFromApplicableToNonApplicable_Unchecks()
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton();

                // Start with applicable product and check the button
                QuickAddControl.UpdateAnticatToggleState("Anwis", btn);
                btn.IsChecked = true;

                // Switch to non-applicable product
                QuickAddControl.UpdateAnticatToggleState("Отлив", btn);

                Assert.Equal(Visibility.Collapsed, btn.Visibility);
                Assert.False(btn.IsChecked);
            });
        }

        [Fact]
        public void UpdateAnticatToggleState_SwitchFromNonApplicableToApplicable_ButtonVisibleAndUnchecked()
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton();

                // Start with non-applicable product
                QuickAddControl.UpdateAnticatToggleState("Отлив", btn);
                Assert.Equal(Visibility.Collapsed, btn.Visibility);

                // Switch to applicable product
                QuickAddControl.UpdateAnticatToggleState("На навесах", btn);

                Assert.Equal(Visibility.Visible, btn.Visibility);
                Assert.False(btn.IsChecked);
            });
        }

        [Fact]
        public void UpdateAnticatToggleState_EmptyProductName_HidesAndUnchecks()
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton { IsChecked = true };
                QuickAddControl.UpdateAnticatToggleState("", btn);

                Assert.Equal(Visibility.Collapsed, btn.Visibility);
                Assert.False(btn.IsChecked);
            });
        }

        [Fact]
        public void UpdateAnticatToggleState_WhitespaceProductName_HidesAndUnchecks()
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton { IsChecked = true };
                QuickAddControl.UpdateAnticatToggleState("   ", btn);

                Assert.Equal(Visibility.Collapsed, btn.Visibility);
                Assert.False(btn.IsChecked);
            });
        }

        // ---- TryParseQuickNumber (regression: «2 150,00» must parse to 2150) ----

        [Theory]
        [InlineData("2 150,00", 2150.0)]
        [InlineData("2 150", 2150.0)]
        [InlineData("2150", 2150.0)]
        [InlineData("2150,5", 2150.5)]
        [InlineData("2150.50", 2150.5)]
        [InlineData("1 360", 1360.0)]
        [InlineData("\u00A02150,00\u00A0", 2150.0)] // non-breaking spaces
        public void TryParseQuickNumber_CatalogFormatWithThousandsSeparator_Parses(string text, double expected)
        {
            // Regression: the price field is auto-filled with MoneyFormatService.Format,
            // e.g. «2 150,00». NumberStyles.Float does not allow the group separator,
            // so the old parse produced 0 and the row was added with a zero price.
            bool ok = QuickAddControl.TryParseQuickNumber(text, out double value);

            Assert.True(ok);
            Assert.Equal(expected, value, 2);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void TryParseQuickNumber_Empty_ReturnsZeroTrue(string? value)
        {
            bool ok = QuickAddControl.TryParseQuickNumber(value, out double result);

            Assert.True(ok);
            Assert.Equal(0, result);
        }

        // ── ResolveQuickAddPrice (v3.48.1 bugfix) ──────────────────────
        // Pure static logic — no STA needed. Regression guard for the bug where
        // a blank or ru-formatted price field («2 150,00») silently produced a
        // row with Price=0 for catalog products (Отлив/Козырёк …), which then
        // dropped out of the «ИТОГО» sum (CalculationViewModel filters Total > 0).

        private static double CatalogPrice(string type, string color) => type switch
        {
            "Отлив" => 2150,
            "Козырёк" => 2150,
            "Anwis" => 1800,
            _ => 0
        };

        [Fact]
        public void ResolveQuickAddPrice_ParsesRuFormattedText()
        {
            // «2 150,00» was the exact failing shape under plain double.TryParse
            // (non-ru culture). Now MoneyFormatService.TryParse normalizes it.
            Assert.Equal(2150, QuickAddControl.ResolveQuickAddPrice("2 150,00", "Отлив", "Белый", CatalogPrice));
        }

        [Theory]
        [InlineData("2150")]
        [InlineData("2 150")]
        [InlineData("2150,5")]
        [InlineData(" 2 150,00 ")]
        public void ResolveQuickAddPrice_ParsesPlainAndTrimmedText(string text)
        {
            double expected = text.Replace(" ", "").Contains(",5") ? 2150.5 : 2150;
            Assert.Equal(expected, QuickAddControl.ResolveQuickAddPrice(text, "Отлив", "Белый", CatalogPrice));
        }

        [Fact]
        public void ResolveQuickAddPrice_BlankText_FallsBackToCatalogPrice()
        {
            Assert.Equal(2150, QuickAddControl.ResolveQuickAddPrice("", "Отлив", "Белый", CatalogPrice));
        }

        [Fact]
        public void ResolveQuickAddPrice_UnparseableText_FallsBackToCatalogPrice()
        {
            Assert.Equal(2150, QuickAddControl.ResolveQuickAddPrice("abc", "Козырёк", "Белый", CatalogPrice));
        }

        [Fact]
        public void ResolveQuickAddPrice_ExplicitZero_FallsBackToCatalogPrice()
        {
            // A deliberate «0» for a catalog-priced product is still treated as
            // missing — the row would otherwise silently vanish from ИТОГО.
            Assert.Equal(1800, QuickAddControl.ResolveQuickAddPrice("0", "Anwis", "Белый", CatalogPrice));
        }

        [Fact]
        public void ResolveQuickAddPrice_NoCatalogEntry_ReturnsZero()
        {
            Assert.Equal(0, QuickAddControl.ResolveQuickAddPrice("", "Работа", "", CatalogPrice));
        }

        [Fact]
        public void ResolveQuickAddPrice_ManualPiece_KeepsUserPrice()
        {
            Assert.Equal(5000, QuickAddControl.ResolveQuickAddPrice("5 000", "Работа", "", CatalogPrice));
        }

        [Fact]
        public void ResolveQuickAddPrice_ManualPieceBlank_ReturnsZero()
        {
            Assert.Equal(0, QuickAddControl.ResolveQuickAddPrice("", "Работа", "", CatalogPrice));
        }

        // ── GetRequiredFieldError (UX#2 validation matrix) ─────────────
        // Pure decision matrix behind the attempt-driven red border. Guards the
        // exact per-type rules: which field blocks the add for which product.

        [Theory]
        [InlineData("Anwis")]
        [InlineData("На навесах")]
        [InlineData("Оконная на метал. крепл.")]
        [InlineData("Дверная сетка")]
        [InlineData("Отлив")]
        [InlineData("Козырёк")]
        [InlineData("Короб")]
        public void GetRequiredFieldError_AreaProduct_MissingWidthBlocks(string type)
        {
            // width=0, height=0 → width is checked first (user-facing priority).
            Assert.Equal(QuickAddControl.QuickAddFieldError.Width,
                QuickAddControl.GetRequiredFieldError(type, width: 0, height: 0, price: 1000));
        }

        [Theory]
        [InlineData("Anwis")]
        [InlineData("Отлив")]
        public void GetRequiredFieldError_AreaProduct_WidthSetMissingHeightBlocks(string type)
        {
            Assert.Equal(QuickAddControl.QuickAddFieldError.Height,
                QuickAddControl.GetRequiredFieldError(type, width: 1200, height: 0, price: 1000));
        }

        [Fact]
        public void GetRequiredFieldError_AreaProduct_DimsSet_NoError()
        {
            Assert.Equal(QuickAddControl.QuickAddFieldError.None,
                QuickAddControl.GetRequiredFieldError("Anwis", width: 1200, height: 1500, price: 1800));
        }

        [Theory]
        [InlineData("ПСУЛ")]
        [InlineData("Уплотнение")]
        public void GetRequiredFieldError_DimsOptionalProducts_NeverBlocksOnDims(string type)
        {
            // ПСУЛ/Уплотнение are cut-to-size materials: zero dims are valid.
            Assert.Equal(QuickAddControl.QuickAddFieldError.None,
                QuickAddControl.GetRequiredFieldError(type, width: 0, height: 0, price: 0));
        }

        [Theory]
        [InlineData("Работа")]
        [InlineData("Откос")]
        [InlineData("Работа за откос")]
        [InlineData("Брус")]
        [InlineData("Пояс")]
        [InlineData("Доставка")]
        public void GetRequiredFieldError_ManualPiece_NeverBlocksOnDims(string type)
        {
            Assert.Equal(QuickAddControl.QuickAddFieldError.None,
                QuickAddControl.GetRequiredFieldError(type, width: 0, height: 0, price: 5000));
        }

        [Fact]
        public void GetRequiredFieldError_Material_MissingPriceBlocks()
        {
            // Материал is the one OptionalQuantity product: manual sum is required.
            Assert.Equal(QuickAddControl.QuickAddFieldError.Price,
                QuickAddControl.GetRequiredFieldError("Материал", width: 0, height: 0, price: 0));
        }

        [Fact]
        public void GetRequiredFieldError_Material_PriceSet_NoError()
        {
            Assert.Equal(QuickAddControl.QuickAddFieldError.None,
                QuickAddControl.GetRequiredFieldError("Материал", width: 0, height: 0, price: 3200));
        }

        [Fact]
        public void GetRequiredFieldError_CustomProduct_NeverBlocks()
        {
            // «Свой товар»: dims/qty/price all optional by design.
            Assert.Equal(QuickAddControl.QuickAddFieldError.None,
                QuickAddControl.GetRequiredFieldError("Свой товар", width: 0, height: 0, price: 0));
        }

        [Fact]
        public void GetRequiredFieldError_Worka_MissingPrice_DoesNotBlock()
        {
            // Manual-piece non-optional-quantity products (Работа…) do NOT
            // require price in the matrix — the row is added with 0 and fixed
            // in the grid. Only Материал demands the sum upfront.
            Assert.Equal(QuickAddControl.QuickAddFieldError.None,
                QuickAddControl.GetRequiredFieldError("Работа", width: 0, height: 0, price: 0));
        }

        [Fact]
        public void SetRequiredHighlight_SetsInvalidTag()
        {
            RunOnStaThread(() =>
            {
                var textBox = new TextBox();

                QuickAddControl.SetRequiredHighlight(textBox);

                Assert.Equal("Invalid", textBox.Tag);
            });
        }

        [Fact]
        public void ClearRequiredHighlight_ClearsOnlyInvalidTag()
        {
            RunOnStaThread(() =>
            {
                var textBox = new TextBox { Tag = "Invalid" };

                QuickAddControl.ClearRequiredHighlight(textBox);
                Assert.Null(textBox.Tag);

                textBox.Tag = "unrelated-state";
                QuickAddControl.ClearRequiredHighlight(textBox);
                Assert.Equal("unrelated-state", textBox.Tag);
            });
        }

        // ─── v3.50: «Повторить» snapshot record ──────────────────────────

        [Fact]
        public void QuickAddSnapshot_Record_PreservesAllFieldValues()
        {
            RunOnStaThread(() =>
            {
                var snap = new QuickAddControl.QuickAddSnapshot(
                    Type: "Anwis", Color: "Белый", Width: "1200", Height: "1500",
                    Qty: "2", Price: "2 150,00", Anticat: true, CustomName: null);

                Assert.Equal("Anwis", snap.Type);
                Assert.Equal("Белый", snap.Color);
                Assert.Equal("1200", snap.Width);
                Assert.Equal("1500", snap.Height);
                Assert.Equal("2", snap.Qty);
                Assert.Equal("2 150,00", snap.Price);
                Assert.True(snap.Anticat);
                Assert.Null(snap.CustomName);
            });
        }

        [Fact]
        public void QuickAddSnapshot_ValueEquality_AllowsRepeatedRestore()
        {
            // The same snapshot may be restored repeatedly (RepeatLastItem has
            // no consume-once semantics) — record equality keeps that safe.
            RunOnStaThread(() =>
            {
                var a = new QuickAddControl.QuickAddSnapshot("Отлив", null, "2000", "", "1", "350,00", false, null);
                var b = new QuickAddControl.QuickAddSnapshot("Отлив", null, "2000", "", "1", "350,00", false, null);

                Assert.Equal(a, b);
            });
        }
    }
}
