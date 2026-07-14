using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// v3.45.0 (Phase 4 refactoring): generic chromeless message dialog
    /// driven by DialogBuilder. Replaces programmatic DialogService dialogs.
    /// </summary>
    public partial class MessageDialogWindow : Window
    {
        /// <summary>Result selected by the user.</summary>
        public object? SelectedResult { get; private set; }

        public MessageDialogWindow(string title, string message, IEnumerable<DialogButton<object>> buttons)
        {
            InitializeComponent();
            Title = title;
            TitleText.Text = title;
            MessageText.Text = message;
            ButtonsPanel.ItemsSource = buttons;
            Loaded += (s, e) => Activate();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyCancelResult();
            Close();
        }

        private void DialogButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: DialogButton<object> button })
            {
                SelectedResult = button.Result;
                Close();
            }
        }

        private void DialogButton_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;
            if (button.Tag is not DialogButton<object> config) return;

            button.FontWeight = config.IsDefault ? FontWeights.SemiBold : FontWeights.Normal;

            if (config.IsDefault)
                button.Focus();

            if (string.IsNullOrEmpty(config.StyleResource))
                return;

            var style = TryFindResource(config.StyleResource) as Style;
            if (style != null)
                button.Style = style;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            var buttons = ButtonsPanel.ItemsSource as IEnumerable<DialogButton<object>>;

            if (e.Key == Key.Escape)
            {
                ApplyCancelResult();
                Close();
                return;
            }

            if (e.Key == Key.Enter)
            {
                var defaultButton = buttons?.FirstOrDefault(b => b.IsDefault);
                if (defaultButton != null)
                {
                    SelectedResult = defaultButton.Result;
                    Close();
                }
            }
        }

        private void ApplyCancelResult()
        {
            var cancelButton = (ButtonsPanel.ItemsSource as IEnumerable<DialogButton<object>>)?.FirstOrDefault(b => b.IsCancel);
            if (cancelButton != null)
                SelectedResult = cancelButton.Result;
        }
    }
}
