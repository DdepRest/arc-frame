using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Вкладка «Обновления» — список версий с историей изменений.
    /// Вынесена из MainWindow.xaml для уменьшения размера главного окна.
    /// Биндится к Updates через DataContext (унаследованный от MainWindow).
    /// </summary>
    public partial class UpdatesTabControl : UserControl
    {
        private MainWindow? _boundWindow;
        private INotifyCollectionChanged? _boundCollection;

        public UpdatesTabControl()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Unloaded += (_, _) => UnsubscribeFromCollection();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UnsubscribeFromCollection();
            _boundWindow = DataContext as MainWindow;
            SubscribeToCollection();
            UpdateCount();
        }

        private void SubscribeToCollection()
        {
            if (_boundWindow?.Updates == null) return;
            _boundCollection = _boundWindow.Updates;
            _boundCollection.CollectionChanged += OnUpdatesChanged;
        }

        private void UnsubscribeFromCollection()
        {
            if (_boundCollection == null) return;
            _boundCollection.CollectionChanged -= OnUpdatesChanged;
            _boundCollection = null;
        }

        private void OnUpdatesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCount();

        private void UpdateCount()
        {
            if (TxtUpdatesCount == null) return;
            int count = _boundWindow?.Updates?.Count ?? 0;
            TxtUpdatesCount.Text = CountText(count);
        }

        /// <summary>
        /// Русская плюрализация: 1 версия, 2-4 версии, 5+ версий
        /// (с учётом особой формы 11-14: 11 версий, 12 версий, …)
        /// </summary>
        private static string CountText(int count)
        {
            int rem10 = count % 10;
            int rem100 = count % 100;
            if (rem100 is >= 11 and <= 14) return $"{count} версий";
            return rem10 switch
            {
                1 => $"{count} версия",
                2 or 3 or 4 => $"{count} версии",
                _ => $"{count} версий"
            };
        }

        // ════════════════════════════════════════════════════════════════════
        // Диагностика связи
        //
        // Проверка обновлений на части машин падала молча: один запрос к
        // raw.githubusercontent.com не переживал таймаут/блокировку провайдера.
        // Кнопка пробует ТРИ канала (raw; api.github.com — запасной без edge-кэша;
        // jsDelivr — не-GitHub CDN, работает даже при полной блокировке GitHub,
        // когда «для обновлений нужен VPN») и показывает точные причины сбоя,
        // чтобы владелец сразу видел, где проблема: в программе, в сети ПК
        // или у провайдера.
        // ════════════════════════════════════════════════════════════════════

        private async void BtnDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            BtnDiagnostics.IsEnabled = false;
            try
            {
                var raw = await UpdateManifestClient.ProbeRawAsync().ConfigureAwait(true);
                var api = await UpdateManifestClient.ProbeApiAsync().ConfigureAwait(true);
                var jsDelivr = await UpdateManifestClient.ProbeJsDelivrAsync().ConfigureAwait(true);

                new DialogBuilder<string>()
                    .Title("Диагностика связи")
                    .Message(BuildDiagnosticsText(raw, api, jsDelivr))
                    .WithButton("Понятно", "ok", isDefault: true, isCancel: true)
                    .ShowDialog(Window.GetWindow(this));
            }
            finally
            {
                BtnDiagnostics.IsEnabled = true;
            }
        }

        private static string UserFacingProbeDetail(UpdateManifestClient.ManifestProbe probe)
        {
            if (probe.Ok)
                return $"доступен ({probe.ElapsedMs} мс)";
            if (probe.Detail.Contains("таймаут", StringComparison.OrdinalIgnoreCase))
                return "нет ответа вовремя";
            if (probe.Detail.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase))
                return "сервис временно недоступен";
            return "нет связи";
        }

        private static string BuildDiagnosticsText(
            UpdateManifestClient.ManifestProbe raw,
            UpdateManifestClient.ManifestProbe api,
            UpdateManifestClient.ManifestProbe jsDelivr)
        {
            static string Mark(UpdateManifestClient.ManifestProbe p) => p.Ok ? "✓" : "✗";

            var sb = new StringBuilder();
            sb.AppendLine($"Проверка каналов обновлений — {DateTime.Now:HH:mm, dd.MM.yyyy}");
            sb.AppendLine();
            sb.AppendLine($"{Mark(raw)} Основной способ связи — {UserFacingProbeDetail(raw)}");
            sb.AppendLine($"{Mark(api)} Запасной способ связи — {UserFacingProbeDetail(api)}");
            sb.AppendLine($"{Mark(jsDelivr)} Дополнительный способ связи — {UserFacingProbeDetail(jsDelivr)}");
            sb.AppendLine();

            if (raw.Ok)
            {
                sb.AppendLine("Вывод: проверка обновлений работает.");
            }
            else if (api.Ok)
            {
                sb.AppendLine("Вывод: основной способ связи недоступен, но запасной работает — " +
                              "обновления доступны.");
            }
            else if (jsDelivr.Ok)
            {
                sb.AppendLine("Вывод: основные способы связи недоступны, но дополнительный работает — " +
                              "обновления доступны. Если установка не начнётся, попробуйте подключение " +
                              "через другую сеть или обратитесь к ответственному за установку.");
            }
            else
            {
                sb.AppendLine("Вывод: ни один способ связи недоступен — проверить обновления не удалось. " +
                              "Проверьте интернет-соединение и попробуйте снова.");
            }

            return sb.ToString();
        }
    }
}
