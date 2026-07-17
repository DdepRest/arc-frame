using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class SidebarControl : UserControl
    {
        public Border CardClientBorder => CardClient;
        public Border CardContractBorder => CardContract;
        public Border CardNotesBorder => CardNotes;
        public TextBox TxtPrefix => TxtContractPrefix;
        public TextBox TxtNumber => TxtContractNumber;
        public ComboBox CmbOrderStatus => CmbStatus;

        public SidebarControl()
        {
            InitializeComponent();
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
            System.Diagnostics.Trace.WriteLine($"[SidebarControl] DataContext is not MainWindow ({DataContext?.GetType().Name ?? "null"}), handler '{handlerName}' skipped.");
            mw = null;
            return false;
        }

        private void TxtContractPrefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(TxtContractPrefix_TextChanged), out var mw)) return;
            if (mw._suppressContractNumberUpdate) return;
            if (!mw.IsInitialized) return;
            mw.SuppressPrefixSave = false;
            mw.UpdateContractNumber();
        }

        private void TxtContractPrefix_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(TxtContractPrefix_LostFocus), out var mw)) return;
            if (mw.SuppressPrefixSave) return;
            AppSettingsService.SaveContractPrefix(TxtContractPrefix.Text);
        }

        private void SelectAll_OnFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(() => tb.SelectAll());
        }

        // ─── Notes formatting toolbar ─────────────────────────────

        private void BtnBold_Click(object sender, RoutedEventArgs e) => WrapSelection("**", "**");
        private void BtnItalic_Click(object sender, RoutedEventArgs e) => WrapSelection("*", "*");

        private void BtnColor_Click(object sender, RoutedEventArgs e)
        {
            ColorPopup.IsOpen = !ColorPopup.IsOpen;
        }

        private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Background is SolidColorBrush brush)
            {
                string hex = $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
                WrapSelection($"[color={hex}]", "[/color]");
                ColorPopup.IsOpen = false;
            }
        }

        private void ColorSwatch_CustomClick(object sender, MouseButtonEventArgs e)
        {
            var lastHex = AppSettingsService.LoadLastColor();
            var defaultColor = System.Drawing.Color.FromArgb(0x00, 0x5F, 0xB8);
            try
            {
                var parsed = System.Drawing.ColorTranslator.FromHtml(lastHex);
                if (!parsed.IsEmpty) defaultColor = parsed;
            }
            catch { /* invalid stored hex — use default */ }

            var dlg = new System.Windows.Forms.ColorDialog
            {
                Color = defaultColor,
                FullOpen = true
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                AppSettingsService.SaveLastColor(hex);
                WrapSelection($"[color={hex}]", "[/color]");
            }
            ColorPopup.IsOpen = false;
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (TxtNotes == null) return;
            var cleaned = System.Text.RegularExpressions.Regex.Replace(TxtNotes.Text,
                @"\[color=[^\]]*\]|\[/color\]|\*\*|\*|^- ", "",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            TxtNotes.Text = cleaned;
            TxtNotes.Focus();
        }

        private void BtnList_Click(object sender, RoutedEventArgs e)
        {
            if (TxtNotes == null) return;
            int start = TxtNotes.SelectionStart;
            int lineStart = TxtNotes.Text.LastIndexOf('\n', start == 0 ? 0 : start - 1) + 1;
            string remainder = lineStart < TxtNotes.Text.Length ? TxtNotes.Text.Substring(lineStart) : "";

            if (remainder.StartsWith("- "))
            {
                TxtNotes.Text = TxtNotes.Text.Remove(lineStart, 2);
                TxtNotes.SelectionStart = Math.Max(0, start - 2);
            }
            else if (remainder.StartsWith("-"))
            {
                TxtNotes.Text = TxtNotes.Text.Remove(lineStart, 1);
                TxtNotes.SelectionStart = Math.Max(0, start - 1);
            }
            else
            {
                TxtNotes.Text = TxtNotes.Text.Insert(lineStart, "- ");
                TxtNotes.SelectionStart = start + 2;
            }
            TxtNotes.Focus();
        }

        private void WrapSelection(string prefix, string suffix)
        {
            if (TxtNotes == null) return;
            int start = TxtNotes.SelectionStart;
            int len = TxtNotes.SelectionLength;

            if (len == 0)
            {
                // No selection — insert empty tag pair so user can type inside.
                TxtNotes.Text = TxtNotes.Text.Insert(start, prefix + suffix);
                TxtNotes.SelectionStart = start + prefix.Length;
                TxtNotes.Focus();
                return;
            }

            string selected = TxtNotes.Text.Substring(start, len);
            bool hasNewlines = selected.Contains('\r') || selected.Contains('\n');

            if (hasNewlines)
            {
                // Wrap each line individually so the line-based parser applies
                // formatting to every line, not just the first one.
                // Empty lines produce e.g. «****» which toggles on/off — net zero,
                // but the line break is preserved.
                var lines = selected.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < lines.Length; i++)
                {
                    sb.Append(prefix).Append(lines[i]).Append(suffix);
                    if (i < lines.Length - 1)
                        sb.Append('\n');
                }

                TxtNotes.Text = TxtNotes.Text.Remove(start, len).Insert(start, sb.ToString());
                TxtNotes.SelectionStart = start;
                TxtNotes.SelectionLength = sb.Length;
            }
            else
            {
                TxtNotes.Text = TxtNotes.Text.Insert(start + len, suffix).Insert(start, prefix);
                TxtNotes.SelectionStart = start + prefix.Length;
                TxtNotes.SelectionLength = len;
            }

            TxtNotes.Focus();
        }

        private void TxtNotes_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (NotesPreview?.Document == null) return;
            NotesPreview.Document.Blocks.Clear();

            foreach (var line in NotesFormatter.Parse(TxtNotes.Text))
            {
                var p = new Paragraph
                {
                    Margin = new Thickness(line.IsListItem ? 12 : 0, 0, 0, 2)
                };

                foreach (var inline in NotesRenderer.ToInlines(line))
                    p.Inlines.Add(inline);

                NotesPreview.Document.Blocks.Add(p);
            }
        }
    }
}
