using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using MosquitoNetCalculator.Models;
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
            ConfigureCollectionView();
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

        private void OnUpdatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateCount();
            if (e.Action == NotifyCollectionChangedAction.Add && _view != null)
            {
                // Runtime-добавление новой версии (AddNewUpdate): новая карточка
                // раскрыта, правило «первые 5» не пересчитываем — старые карточки
                // не должны дёргаться.
                foreach (UpdateItem item in e.NewItems!.OfType<UpdateItem>())
                    item.IsExpanded = true;
            }
        }

        private void UpdateCount()
        {
            if (TxtUpdatesCount == null) return;
            int total = _boundWindow?.Updates?.Count ?? 0;
            TxtUpdatesCount.Text = UpdatesListLogic.ClassicCountText(total);
        }

        // ════════════════════════════════════════════════════════════════════
        // v3.51: фильтры (чипы типа + поиск), счётчик «N из M», свёрнутые
        // карточки, копирование и «Наверх». Чистая логика — UpdatesListLogic.
        // ════════════════════════════════════════════════════════════════════

        private ListCollectionView? _view;
        private bool _filterIsProgrammatic;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            ConfigureCollectionView();
        }

        private void ConfigureCollectionView()
        {
            var updates = _boundWindow?.Updates;
            if (updates == null) return;

            _view = UpdatesListLogic.ConfigureView(updates);
            _view.Filter = o => ApplyFilter(o);
        }

        private bool ApplyFilter(object o)
        {
            if (o is not UpdateItem item || _boundWindow?.Updates == null) return false;

            string type = ActiveTypeFilter();
            string query = TxtUpdatesSearch?.Text ?? string.Empty;
            bool matches = UpdatesListLogic.BuildPredicate(type, query)(item);

            // Пока фильтр/поиск активен — отфильтрованные карточки раскрыты,
            // иначе поиск по свёрнутым телам нечитаем.
            if (matches && (type.Length > 0 || query.Trim().Length > 0))
                item.IsExpanded = true;
            return matches;
        }

        private string ActiveTypeFilter()
        {
            if (ChipFilterNovelty?.IsChecked == true) return "Новинка";
            if (ChipFilterImprovement?.IsChecked == true) return "Улучшение";
            if (ChipFilterFix?.IsChecked == true) return "Исправление";
            return string.Empty;
        }

        private void RefreshFilter()
        {
            if (_view == null) ConfigureCollectionView();
            if (_view == null) return;

            _view.Refresh();

            var updates = _boundWindow?.Updates;
            int total = updates?.Count ?? 0;
            int visible = _view.Count;
            TxtUpdatesCount.Text = UpdatesListLogic.CountText(visible, total);
            UpdatesEmptyState.Visibility = visible == 0 && total > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ChipTypeFilter_Click(object sender, RoutedEventArgs e)
        {
            if (_filterIsProgrammatic) return;

            // Чипы — взаимоисключающие: клик по активному чипу снимает его
            // («Все» снова активно). IsChecked прямо в обработчике не вернуть,
            // поэтому снимаем через Dispatcher.
            var clicked = (ToggleButton)sender;
            if (clicked.IsChecked != true)
            {
                _filterIsProgrammatic = true;
                ChipFilterAll.IsChecked = true;
                _filterIsProgrammatic = false;
            }
            else if (!ReferenceEquals(clicked, ChipFilterAll))
            {
                _filterIsProgrammatic = true;
                ChipFilterAll.IsChecked = false;
                _filterIsProgrammatic = false;
            }

            RefreshFilter();
        }

        private void TxtUpdatesSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatesSearchPlaceholder.Visibility = TxtUpdatesSearch.Text.Length == 0
                ? Visibility.Visible : Visibility.Collapsed;
            BtnClearUpdatesSearch.Visibility = TxtUpdatesSearch.Text.Length == 0
                ? Visibility.Collapsed : Visibility.Visible;
            RefreshFilter();
        }

        private void BtnClearUpdatesSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtUpdatesSearch.Clear();
            TxtUpdatesSearch.Focus();
        }

        private void CardHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is UpdateItem item)
                item.IsExpanded = !item.IsExpanded;
        }

        private void CopyCard_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is UpdateItem item)
            {
                try { Clipboard.SetText(UpdatesListLogic.BuildCopyText(item)); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Updates] clipboard failed: {ex}"); }
            }
        }

        private void BtnScrollTop_Click(object sender, RoutedEventArgs e)
        {
            UpdatesScroll?.ScrollToHome();
        }

        private void UpdatesScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (BtnScrollTop == null) return;
            BtnScrollTop.Visibility = e.VerticalOffset > UpdatesScroll.ViewportHeight
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ════════════════════════════════════════════════════════════════════
        // Диагностика связи

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
                return $"работает, {probe.ElapsedMs} мс";
            if (probe.Detail.Contains("таймаут", StringComparison.OrdinalIgnoreCase))
                return "нет ответа вовремя";
            if (probe.Detail.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase))
                return "сервис временно недоступен";
            return "нет связи";
        }

        /// <summary>
        /// v3.51: текст диагностики выровнен по колонкам — короткое имя канала,
        /// затем точка-статус. Длинные канцелярские подписи («Основной способ
        /// связи») переносились на 2-3 строки и сливались в кашу.
        /// </summary>
        private static string BuildDiagnosticsText(
            UpdateManifestClient.ManifestProbe raw,
            UpdateManifestClient.ManifestProbe api,
            UpdateManifestClient.ManifestProbe jsDelivr)
        {
            static string Line(string channel, UpdateManifestClient.ManifestProbe p)
            {
                string mark = p.Ok ? "✓" : "✗";
                return $"{mark} {channel}: {UserFacingProbeDetail(p)}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Проверка каналов — {DateTime.Now:HH:mm, dd.MM.yyyy}");
            sb.AppendLine();
            sb.AppendLine(Line("Основной", raw));
            sb.AppendLine(Line("Запасной", api));
            sb.AppendLine(Line("Дополн. (jsDelivr)", jsDelivr));
            sb.AppendLine();
            sb.AppendLine("— — — — — — — — — — — —");
            sb.AppendLine();

            if (raw.Ok)
            {
                sb.AppendLine("Проверка обновлений работает.");
            }
            else if (api.Ok)
            {
                sb.AppendLine("Основной канал недоступен, но запасной работает — " +
                              "обновления доступны.");
            }
            else if (jsDelivr.Ok)
            {
                sb.AppendLine("Основные каналы недоступны, но дополнительный работает — " +
                              "обновления доступны. Если установка не начнётся, " +
                              "попробуйте другую сеть или VPN.");
            }
            else
            {
                sb.AppendLine("Ни один канал недоступен — проверить обновления не удалось. " +
                              "Проверьте интернет-соединение и попробуйте снова.");
            }

            return sb.ToString();
        }
    }
}
