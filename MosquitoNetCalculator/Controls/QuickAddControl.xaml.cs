using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class QuickAddControl : UserControl
    {
        private bool _updatingQuickCombo;

        public Border CardQuickAddBorder => CardQuickAdd;
        public ComboBox CmbType => CmbQuickType;
        public ComboBox CmbColor => CmbQuickColor;
        public TextBox TxtWidth => TxtQuickWidth;
        public TextBox TxtHeight => TxtQuickHeight;
        public TextBox TxtQty => TxtQuickQty;
        public TextBox TxtPrice => TxtQuickPrice;
        public TextBox TxtSearch => TxtQuickSearch;
        public Popup SearchDrop => SearchPopup;
        public ListBox Suggestions => SearchSuggestions;
        public Border Preview => PreviewChip;
        public TextBlock PreviewText => TxtQuickPreview;
        /// <summary>Currently selected AnwisSizeMode — persisted across «Добавить» clicks, reset on new order.</summary>
        public Models.AnwisSizeMode SelectedAnwisMode { get; private set; } = AnwisSizeService.DefaultMode;

        public QuickAddControl()
        {
            InitializeComponent();

            // Right-click on the Type dropdown opens the Anwis mode context menu —
            // zero pixels of permanent workspace footprint, discoverable via ToolTip.
            CmbQuickType.PreviewMouseRightButtonDown += CmbQuickType_PreviewMouseRightButtonDown;

            // ToolTips with mode descriptions.
            ToolTipService.SetShowDuration(PillBB60, 15000);
            ToolTipService.SetShowDuration(PillBB70, 15000);
            ToolTipService.SetShowDuration(PillPP, 15000);
            ToolTipService.SetShowDuration(PillProem, 15000);
            ToolTipService.SetShowDuration(PillGab, 15000);
            PillBB60.ToolTip  = AnwisSizeService.HintTexts[AnwisSizeMode.Брусбокс60];
            PillBB70.ToolTip  = AnwisSizeService.HintTexts[AnwisSizeMode.Брусбокс70];
            PillPP.ToolTip    = AnwisSizeService.HintTexts[AnwisSizeMode.Профипласт];
            PillProem.ToolTip = AnwisSizeService.HintTexts[AnwisSizeMode.РазмерПроёма];
            PillGab.ToolTip   = AnwisSizeService.HintTexts[AnwisSizeMode.Габаритный];

            // Hover effects for segmented control segments.
            PillBB60.MouseEnter  += (_, _) => HoverSegment(PillBB60,  AnwisSizeMode.Брусбокс60);
            PillBB60.MouseLeave += (_, _) => UpdateAnwisModePills();
            PillBB70.MouseEnter  += (_, _) => HoverSegment(PillBB70,  AnwisSizeMode.Брусбокс70);
            PillBB70.MouseLeave += (_, _) => UpdateAnwisModePills();
            PillPP.MouseEnter    += (_, _) => HoverSegment(PillPP,    AnwisSizeMode.Профипласт);
            PillPP.MouseLeave   += (_, _) => UpdateAnwisModePills();
            PillProem.MouseEnter += (_, _) => HoverSegment(PillProem, AnwisSizeMode.РазмерПроёма);
            PillProem.MouseLeave += (_, _) => UpdateAnwisModePills();
            PillGab.MouseEnter   += (_, _) => HoverSegment(PillGab,   AnwisSizeMode.Габаритный);
            PillGab.MouseLeave  += (_, _) => UpdateAnwisModePills();
        }

        /// <summary>
        /// Resolves the parent MainWindow from DataContext, logging a diagnostic if the
        /// DataContext is not a MainWindow. Returns false in that case so callers can
        /// bail out gracefully.
        /// </summary>
        private bool TryGetMainWindow(string handlerName, [NotNullWhen(true)] out MainWindow? mw)
        {
            if (DataContext is MainWindow window)
            {
                mw = window;
                return true;
            }
            System.Diagnostics.Trace.WriteLine($"[QuickAddControl] DataContext is not MainWindow ({DataContext?.GetType().Name ?? "null"}), handler '{handlerName}' skipped.");
            mw = null;
            return false;
        }

        private void QuickField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                // Автозамена десятичной точки на запятую (0.65 → 0,65):
                // русская раскладка/цифровой блок дают точку, а парсер ждёт запятую.
                if (tb.Text.Contains('.') && !tb.Text.Contains(','))
                {
                    int caret = tb.CaretIndex;
                    tb.Text = tb.Text.Replace('.', ',');
                    tb.CaretIndex = Math.Min(caret, tb.Text.Length);
                }
                UpdateQuickPreview();
                // UX#2: Clear required-field highlight when user starts typing
                ClearRequiredHighlight(tb);
            }
        }

        /// <summary>
        /// Парсит числовое поле панели добавления: допускает и точку, и запятую
        /// как десятичный разделитель, и пробел как разделитель тысяч (поле
        /// цены авто-заполняется через MoneyFormatService.Format → «2 150,00»,
        /// а NumberStyles.Float такой текст не распознаёт). Пустая строка → 0 (true).
        /// </summary>
        internal static bool TryParseQuickNumber(string? text, out double value)
        {
            if (string.IsNullOrWhiteSpace(text)) { value = 0; return true; }
            return MoneyFormatService.TryParse(text, out value);
        }

        private void QuickField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) QuickAddItem();
        }

        private void QuickField_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(() => tb.SelectAll());
        }

        // UX#2: Re-apply required-field highlight when user leaves empty field
        // Required-field red-border highlighting was removed on user request:
        // no block/field should be painted red. The empty-field notices below
        // are no-ops so any existing call site stays valid without visual effect.
        private void QuickField_LostFocus(object sender, RoutedEventArgs e)
        {
        }

        internal void HighlightRequiredIfEmpty()
        {
        }

        private void SetRequiredHighlight(TextBox tb)
        {
        }

        private void ClearRequiredHighlight(TextBox tb)
        {
            var border = Application.Current?.TryFindResource("Border") as SolidColorBrush;
            if (border != null) tb.BorderBrush = border;
        }

    }
}
