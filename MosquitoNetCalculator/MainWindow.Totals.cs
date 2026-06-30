using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
        internal void UpdateTotal()
        {
            // Debounce: reset timer on every call so rapid property changes batch into one update
            if (_updateTotalDebounceTimer != null)
            {
                _updateTotalDebounceTimer.Stop();
                _updateTotalDebounceTimer.Start();
                return;
            }

            _updateTotalDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50),
                IsEnabled = true
            };
            _updateTotalDebounceTimer.Tick += (_, _) =>
            {
                _updateTotalDebounceTimer?.Stop();
                ExecuteUpdateTotal();
            };
            _updateTotalDebounceTimer.Start();
        }

        private void ExecuteUpdateTotal()
        {
            // Already on the UI thread (called from DispatcherTimer.Tick); no need to
            // re-dispatch via Dispatcher.Invoke — that was a no-op redirect to self
            // and a reentrancy hazard if the UI thread was already busy.
            var info = ViewModel.CalcVM.CalculateTotal(ClientInfo.AdditionalKpsTotal);

            if (TotalCardControl?.TotalRun != null)
                TotalCardControl.TotalRun.Text = MoneyFormatService.Format(info.Total);

            if (TotalCardControl?.TotalSub != null)
            {
                if (info.Count == 0 && ClientInfo.AdditionalKpsTotal <= 0)
                {
                    TotalCardControl.TotalSub.Text = "";
                }
                else
                {
                    var parts = new List<string>();
                    if (info.TotalArea > 0) parts.Add($"{info.TotalArea:F3} м\u00B2");
                    if (info.TotalLinear > 0) parts.Add($"{info.TotalLinear:F3} м.п.");
                    if (info.TotalPieces > 0) parts.Add($"{info.TotalPieces} шт.");
                    if (ClientInfo.AdditionalKpsTotal > 0) parts.Add($"доп. КП {MoneyFormatService.Format(ClientInfo.AdditionalKpsTotal)} руб.");
                    TotalCardControl.TotalSub.Text = $"{info.Count} поз." + (parts.Count > 0 ? ", " + string.Join(", ", parts) : "");
                }
            }

            if (TotalCardControl?.AmountWords != null)
                TotalCardControl.AmountWords.Text = info.Total > 0
                    ? AmountInWordsService.Convert(info.Total)
                    : "";
        }
    }
}
