using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MosquitoNetCalculator.Controls
{
    public partial class QuickAddControl
    {
        private void TxtQuickSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            BtnClearQuickSearch.Visibility = string.IsNullOrEmpty(TxtQuickSearch.Text)
                ? Visibility.Collapsed : Visibility.Visible;
            UpdateSearchSuggestions();
        }

        private void BtnClearQuickSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtQuickSearch.Text = string.Empty;
            SearchPopup.IsOpen = false;
            TxtQuickSearch.Focus();
        }

        private void TxtQuickSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (!SearchPopup.IsOpen) return;
            if (e.Key == Key.Down)
            {
                if (SearchSuggestions.SelectedIndex < SearchSuggestions.Items.Count - 1)
                    SearchSuggestions.SelectedIndex++;
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (SearchSuggestions.SelectedIndex > 0)
                    SearchSuggestions.SelectedIndex--;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                // If the user hasn't navigated with arrows yet, pick the first suggestion.
                string? sel = SearchSuggestions.SelectedItem as string;
                if (sel == null && SearchSuggestions.Items.Count > 0)
                    sel = SearchSuggestions.Items[0] as string;

                if (sel != null)
                {
                    CmbQuickType.SelectedItem = sel;
                    SearchPopup.IsOpen = false;
                    TxtQuickSearch.Text = "";
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                SearchPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void SearchSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void SearchSuggestions_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep != SearchSuggestions)
            {
                if (dep is ListBoxItem item)
                {
                    CmbQuickType.SelectedItem = item.Content;
                    SearchPopup.IsOpen = false;
                    TxtQuickSearch.Text = "";
                    return;
                }
                dep = VisualTreeHelper.GetParent(dep);
            }
        }

        private void UpdateSearchSuggestions()
        {
            if (!TryGetMainWindow(nameof(UpdateSearchSuggestions), out var mw)) return;

            string searchText = TxtQuickSearch.Text?.Trim().ToLower() ?? "";

            if (string.IsNullOrEmpty(searchText))
            {
                SearchSuggestions.ItemsSource = mw.ProductNames;
                SearchSuggestions.SelectedIndex = -1;
                SearchPopup.IsOpen = true;
            }
            else
            {
                var filtered = mw.ProductNames
                    .Where(n => n.ToLower().Contains(searchText))
                    .ToList();

                SearchSuggestions.ItemsSource = filtered;
                SearchSuggestions.SelectedIndex = -1;
                SearchPopup.IsOpen = filtered.Count > 0;
            }

            // No SelectedIndex assignment here — the ListBox never steals
            // focus from the search TextBox. The user navigates with Up/Down
            // keys and confirms with Enter (which auto-selects the first item
            // if nothing is selected yet).
        }

        private void SelectAll_OnFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(() => tb.SelectAll());
        }

        private void TxtQuickSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            // Close popup when search field loses focus (e.g. click outside)
            // Small delay to allow click on ListBoxItem to fire first
            Dispatcher.BeginInvoke(() => SearchPopup.IsOpen = false,
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
