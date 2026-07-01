using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
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

        private static void ToggleSection(TextBlock chevron, StackPanel content)
        {
            content.BeginAnimation(OpacityProperty, null);
            if (content.Visibility == Visibility.Visible)
            {
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                fadeOut.Completed += (_, _) => content.Visibility = Visibility.Collapsed;
                content.BeginAnimation(OpacityProperty, fadeOut);
                chevron.Text = "\u25B6";
            }
            else
            {
                content.Visibility = Visibility.Visible;
                content.Opacity = 0;
                var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                content.BeginAnimation(OpacityProperty, fadeIn);
                chevron.Text = "\u25BC";
            }
        }

        private void CardClient_HeaderClick(object sender, MouseButtonEventArgs e)
        {
            ToggleSection(ChevronClient, ContentClient);
            e.Handled = true;
        }

        private void CardContract_HeaderClick(object sender, MouseButtonEventArgs e)
        {
            ToggleSection(ChevronContract, ContentContract);
            e.Handled = true;
        }

        private void CardNotes_HeaderClick(object sender, MouseButtonEventArgs e)
        {
            ToggleSection(ChevronNotes, ContentNotes);
            e.Handled = true;
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
    }
}
