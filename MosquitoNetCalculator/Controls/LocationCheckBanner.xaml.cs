using System;
using System.Windows;
using System.Windows.Controls;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Тонкая полоса под заголовком главного окна: «Местоположение: … ·
    /// префикс N» с действиями «Всё верно» / «Изменить» / «✕».
    ///
    /// Контрол не знает ни про настройки, ни про MainWindow — он только
    /// поднимает события <see cref="Confirmed"/>, <see cref="ChangeRequested"/>,
    /// <see cref="Snoozed"/>. Запись отметок и открытие окна выбора живут в
    /// MainWindow.LocationCheck.cs, поэтому контрол тестируется в изоляции.
    /// </summary>
    public partial class LocationCheckBanner : UserControl
    {
        /// <summary>Пользователь нажал «Всё верно» (продлить тишину на 30 дней).</summary>
        public event EventHandler? Confirmed;

        /// <summary>Пользователь нажал «Изменить» (открыть выбор местоположения).</summary>
        public event EventHandler? ChangeRequested;

        /// <summary>Пользователь нажал «✕» (отложить проверку на 3 дня).</summary>
        public event EventHandler? Snoozed;

        public LocationCheckBanner()
        {
            InitializeComponent();
        }

        /// <summary>Сырые значения последнего вызова <see cref="Show"/>.</summary>
        private string? _locationName;
        private string? _prefix;

        /// <summary>Готовая сводка «офис · префикс N» — для тестов и отладки.</summary>
        public string DisplayText
            => Services.LocationCheckService.BuildSummary(_locationName, _prefix);

        /// <summary>Название офиса, как оно видно в полосе.</summary>
        public string LocationText => TxtLocation.Text;

        /// <summary>Текст чипа префикса («префикс 2») или пустая строка.</summary>
        public string PrefixText => PrefixChip.Visibility == Visibility.Visible ? TxtPrefix.Text : string.Empty;

        public Button ConfirmButton => BtnConfirm;
        public Button ChangeButton => BtnChange;
        public Button SnoozeButton => BtnSnooze;

        /// <summary>
        /// Показывает баннер с текущим местоположением. Название и префикс —
        /// два разных факта, поэтому в полосе они разведены: офис подписью,
        /// префикс — отдельным чипом (в тексте он дублировал бы себя).
        /// </summary>
        public void Show(string? locationName, string? prefix)
        {
            string name = (locationName ?? string.Empty).Trim();
            string prefixText = (prefix ?? string.Empty).Trim();
            _locationName = name;
            _prefix = prefixText;

            TxtLocation.Text = string.IsNullOrEmpty(name) ? "не задано" : name;

            bool hasPrefix = !string.IsNullOrEmpty(prefixText);
            TxtPrefix.Text = hasPrefix ? $"префикс {prefixText}" : string.Empty;
            PrefixChip.Visibility = hasPrefix ? Visibility.Visible : Visibility.Collapsed;

            // Диктор читает полосу одной фразой — иначе чип и тексты
            // озвучиваются по отдельности и теряется, к какому офису префикс.
            System.Windows.Automation.AutomationProperties.SetName(
                this,
                "Проверка местоположения: " + Services.LocationCheckService.BuildSummary(name, prefixText));

            Visibility = Visibility.Visible;
        }

        /// <summary>Скрывает баннер (после любого ответа пользователя).</summary>
        public void Hide() => Visibility = Visibility.Collapsed;

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
            => Confirmed?.Invoke(this, EventArgs.Empty);

        private void BtnChange_Click(object sender, RoutedEventArgs e)
            => ChangeRequested?.Invoke(this, EventArgs.Empty);

        private void BtnSnooze_Click(object sender, RoutedEventArgs e)
            => Snoozed?.Invoke(this, EventArgs.Empty);
    }
}
