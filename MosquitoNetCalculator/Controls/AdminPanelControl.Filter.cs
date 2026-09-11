using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Частичный класс админ-панели: ПОИСК, ФИЛЬТРЫ ПО СТАТУСУ и ГРУППИРОВКА
    /// списка офисов («Обновления»). Логика фильтрации вынесена в чистый
    /// <see cref="OfficeRowFilter"/> (покрыт юнит-тестами), здесь — только
    /// связка ICollectionView ↔ UI и состояние чипов.
    /// </summary>
    public partial class AdminPanelControl
    {
        /// <summary>Активный фильтр статуса: All / Outdated / UpToDate / NoData.</summary>
        private string _statusFilter = "All";

        /// <summary>Представление списка с группировкой и фильтром (источник ItemsControl).</summary>
        public ICollectionView GroupedRows { get; private set; } = null!;

        /// <summary>Русские заголовки групп по статусу офиса.</summary>
        private static readonly IReadOnlyDictionary<OfficeStatus, string> GroupTitles =
            new Dictionary<OfficeStatus, string>
            {
                [OfficeStatus.Outdated] = "УСТАРЕВШИЕ",
                [OfficeStatus.UpToDate] = "АКТУАЛЬНЫЕ",
                [OfficeStatus.NoData] = "НЕТ ДАННЫХ",
            };

        /// <summary>
        /// Строит ICollectionView над Rows: группировка по статусу офиса,
        /// фильтр = поиск по названию + выбранный чип статуса.
        /// Вызывается один раз из конструктора (после InitializeComponent).
        /// </summary>
        private void InitializeOfficesView()
        {
            GroupedRows = CollectionViewSource.GetDefaultView(Rows);
            GroupedRows.Filter = ApplyRowFilter;
            GroupedRows.GroupDescriptions.Add(new PropertyGroupDescription(
                nameof(OfficeStatusRow.Status),
                converter: new OfficeStatusGroupConverter()));

            // Без этого все четыре чипа несут свой Tag из XAML и РИСУЮТСЯ
            // активными одновременно; выставляем визуальное состояние
            // «активен только текущий фильтр».
            RefreshChipVisuals();
        }

        /// <summary>
        /// Фильтр строки: поисковый текст (название офиса / префикс, без регистра)
        /// И выбранный чип статуса. Чистая логика — в <see cref="OfficeRowFilter"/>.
        /// </summary>
        private bool ApplyRowFilter(object item)
        {
            if (item is not OfficeStatusRow row)
                return false;

            return OfficeRowFilter.Matches(
                row, TxtSearch.Text, _statusFilter);
        }

        /// <summary>Обновляет строки панели и перезачитывает группировку/фильтр.
        /// Заодно подчищает маркер «недавно отвязаны»: устройство, вернувшееся
        /// с отчётом, больше не числится отвязанным. internal — точка сборки
        /// для тестов реальной поверхности (без reflection).</summary>
        internal void ReplaceRows(IEnumerable<OfficeStatusRow> rows)
        {
            Rows.Clear();
            foreach (var row in rows)
                Rows.Add(row);
            GroupedRows?.Refresh();
            PruneJustUnbound();
        }

        /// <summary>TextChanged поля поиска: применить фильтр и показать/скрыть «✕».
        /// Обновляем и пустые состояния: поиск, не совпавший ни с чем, должен
        /// показывать «Ничего не найдено», а не пустой список (ловил playtest).</summary>
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            BtnClearSearch.Visibility = TxtSearch.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            GroupedRows?.Refresh();
            UpdateEmptyStates();
        }

        /// <summary>«✕» в поле поиска: очистить и вернуть фокус.</summary>
        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Clear();
            TxtSearch.Focus();
        }

        /// <summary>Клик по чипу-фильтру статуса: радио-переключение и рефреш.
        /// Значение фильтра определяем ПО ССЫЛКЕ на чип, а не по Tag: Tag —
        /// только визуальное состояние (активному чипу выставляет его
        /// RefreshChipVisuals, остальным — «»), читать его для логики нельзя —
        /// иначе после второго клика фильтр навсегда становится «» и список
        /// пустеет (ловил playtest).</summary>
        private void ChipFilter_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.Border chip)
                return;
            ApplyChipFilter(chip);
            e.Handled = true;
        }

        /// <summary>UX-10: чипы доступны с клавиатуры — Enter/Space активируют
        /// выбранный фильтр (та же логика, что по клику).</summary>
        private void ChipFilter_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter && e.Key != System.Windows.Input.Key.Space)
                return;
            if (sender is not System.Windows.Controls.Border chip)
                return;
            ApplyChipFilter(chip);
            e.Handled = true;
        }

        private void ApplyChipFilter(System.Windows.Controls.Border chip)
        {
            _statusFilter =
                chip == ChipFilterAll ? "All" :
                chip == ChipFilterOutdated ? "Outdated" :
                chip == ChipFilterUpToDate ? "UpToDate" :
                chip == ChipFilterNoData ? "NoData" : _statusFilter;

            RefreshChipVisuals();
            GroupedRows?.Refresh();
            UpdateEmptyStates();
        }

        /// <summary>
        /// Чипы подсвечиваются через Trigger на собственном Tag (см. XAML):
        /// активному чипу выставляем Tag = его значению, остальным — «».
        /// </summary>
        private void RefreshChipVisuals()
        {
            SetChipState(ChipFilterAll, "All");
            SetChipState(ChipFilterOutdated, "Outdated");
            SetChipState(ChipFilterUpToDate, "UpToDate");
            SetChipState(ChipFilterNoData, "NoData");
        }

        private void SetChipState(System.Windows.Controls.Border chip, string value)
            => chip.Tag = _statusFilter == value ? value : "";

        /// <summary>
        /// Чистая логика фильтра строк админ-панели — покрыта юнит-тестами
        /// (AdminPanelFilterTests). Отделена от UI, чтобы тестировать без WPF.
        /// </summary>
        internal static class OfficeRowFilter
        {
            /// <summary>
            /// True, когда строка проходит оба условия: поисковый текст
            /// (пустой/пробельный = любой офис) и фильтр статуса («All» = любой).
            /// Поиск идёт по названию офиса и префиксу, без регистра культуры.
            /// </summary>
            public static bool Matches(OfficeStatusRow row, string searchText, string statusFilter)
            {
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var query = searchText.Trim();
                    var inName = row.LocationName?.IndexOf(query, StringComparison.InvariantCultureIgnoreCase) >= 0;
                    var inPrefix = row.Prefix?.IndexOf(query, StringComparison.InvariantCultureIgnoreCase) >= 0;
                    if (!inName && !inPrefix)
                        return false;
                }

                return statusFilter switch
                {
                    "Outdated" => row.Status == OfficeStatus.Outdated,
                    "UpToDate" => row.Status == OfficeStatus.UpToDate,
                    "NoData" => row.Status == OfficeStatus.NoData,
                    _ => true,
                };
            }
        }

        /// <summary>
        /// Группировка строк по статусу: OfficeStatus → русский заголовок группы
        /// («УСТАРЕВШИЕ», «АКТУАЛЬНЫЕ», «НЕТ ДАННЫХ»).
        /// </summary>
        private sealed class OfficeStatusGroupConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => value is OfficeStatus s && GroupTitles.TryGetValue(s, out var title)
                    ? title
                    : "ПРОЧИЕ";

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => throw new NotSupportedException();
        }
    }
}
