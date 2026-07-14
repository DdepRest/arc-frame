using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// v3.43.5: строка сводки расхода материалов (отображается в Card 3).
    /// v3.44.5: добавлен подробный tooltip для экономии.
    /// </summary>
    public class MaterialSummaryRow
    {
        public string Name { get; set; } = "";
        /// <summary>Детализация per-window: "1.545 м²" или "×3"</summary>
        public string PerDetail { get; set; } = "";
        /// <summary>Итоговое количество: "4.635 м²"</summary>
        public string TotalDisplay { get; set; } = "";
        /// <summary>Зелёный чип экономии: "экон. −1 050 ₽"</summary>
        public string Note { get; set; } = "";
        public bool HasNote => !string.IsNullOrEmpty(Note);
        /// <summary>Подробный tooltip с расчётом экономии.</summary>
        public string? EconomyTooltip { get; set; }
        public bool HasEconomyTooltip => !string.IsNullOrEmpty(EconomyTooltip);
    }
    public partial class SlopePanelControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private SlopeCalculation? _currentCalculation;
        private PriceService? _priceService;

        // ─── Edit-mode state (v3.43.5) ────────────────────────────
        private bool _isEditMode;
        private OrderItem? _editMaterialItem;
        private OrderItem? _editLaborItem;

        /// <summary>Текущий расчёт (для кнопки «Добавить в КП»).</summary>
        public SlopeCalculation? CurrentCalculation => _currentCalculation;

        /// <summary>
        /// Общее количество откосов в заказе (БЕЗ учёта текущей панели).
        /// Используется для расчёта экономии герметика/скотча.
        /// </summary>
        public int TotalWindowCountInOrder { get; set; }

        /// <summary>Цены для расчёта (из PriceService).</summary>
        public (double Sandwich, double Foam, double Sealant, double Tape, double Start, double FProfile, double Penoplex, double Labor)
            Prices { get; set; } = (1200, 750, 350, 135, 135, 250, 450, 600);

        /// <summary>Заголовок карточки сводки: "Итого на N откосов"</summary>
        public string SummaryTitle => _currentCalculation != null
            ? $"Итого на {_currentCalculation.WindowCount} откос{(GetRussianPlural(_currentCalculation.WindowCount))}"
            : "Итого";

        /// <summary>
        /// v3.43.3 (review fix): возвращает НЕ пустой мусорный SlopeMaterial(),
        /// а сам _currentCalculation.Labor (или null, если расчёта ещё нет).
        /// Binding на TextBoxes Труда через RelativeSource={AncestorType=UserControl}
        /// пере-эвальюирует на каждом OnPropertyChanged(nameof(Labor)) — если бы
        /// возвращался новый SlopeMaterial(), любой mid-edit push улетал бы в
        /// «ничейную» пустышку и терялся.
        /// </summary>
        /// <summary>
        /// v3.43.3 (review fix): true когда есть валидный расчёт — для IsEnabled на TextBox'ах Труда.
        /// При null WPF пушит в null source → ошибка BindingExpression; блокируем IsEnabled,
        /// чтобы push не уходил и binding был чистым.
        /// </summary>
        public bool HasCalculation => _currentCalculation != null;

        /// <summary>
        /// v3.43.3 (review fix #2): возвращает _currentCalculation.Labor или null (если расчёта нет).
        /// Binding на Labor.Quantity/Price через RelativeSource получает null → TargetNullValue=''
        /// в XAML делает TextBox пустым, а IsEnabled={Binding HasCalculation} блокирует ввод.
        /// </summary>
        public SlopeMaterial? Labor => _currentCalculation?.Labor;

        /// <summary>
        /// v3.44.0: возвращает _currentCalculation.LaminatinaLabor или null (если расчёта нет).
        /// </summary>
        public SlopeMaterial? LaminatinaLabor => _currentCalculation?.LaminatinaLabor;

        public SlopePanelControl()
        {
            InitializeComponent();
            UpdateAddButtonState();

            // BETA banner: hide if the user previously dismissed it.
            BetaBanner.Visibility = AppSettingsService.IsSlopeBetaBannerHidden()
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void NumWindowCount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
        {
            // v3.43.4: NumericUpDownControl.ValueChanged → UpdateCalculation.
            // Раньше был TextBox.TextChanged на отдельном поле «Кол-во окон»;
            // NumericUpDown держит значение в Value (int), и подписчик реагирует
            // на изменение через routed event — чище, чем парсить Text каждый раз.
            if (!IsInitialized) return;
            UpdateCalculation();
        }

        /// <summary>
        /// Закрывает BETA-предупреждение и сохраняет флаг, чтобы оно больше
        /// не показывалось при следующих открытиях панели откосов.
        /// </summary>
        private void BtnCloseBetaBanner_Click(object sender, RoutedEventArgs e)
        {
            BetaBanner.Visibility = Visibility.Collapsed;
            AppSettingsService.HideSlopeBetaBanner();
        }

        /// <summary>
        /// Устанавливает сервис цен для получения актуальных цен материалов.
        /// </summary>
        public void SetPriceService(PriceService? service)
        {
            _priceService = service;
        }

        /// <summary>
        /// v3.43.5: загружает существующий откос для редактирования.
        /// Клонирует SlopeData для изоляции — изменения в панели не затрагивают
        /// DataGrid, пока пользователь не нажмёт «Сохранить».
        /// </summary>
        public void LoadForEdit(OrderItem materialItem, OrderItem laborItem)
        {
            _isEditMode = true;
            _editMaterialItem = materialItem;
            _editLaborItem = laborItem;

            var sd = materialItem.SlopeData;
            if (sd == null)
            {
                _currentCalculation = null;
                Reset();
                return;
            }

            // Клонируем — правки в панели не трогают оригинал до сохранения
            _currentCalculation = sd.DeepClone();

            TxtWidth.Text = sd.WidthMm.ToString();
            TxtHeight.Text = sd.HeightMm.ToString();
            TxtDepth.Text = (sd.DepthM * 1000).ToString();
            NumWindowCount.Value = sd.WindowCount;

            MaterialItems.ItemsSource = new List<SlopeMaterial>
            {
                _currentCalculation.Sandwich,
                _currentCalculation.Foam,
                _currentCalculation.Sealant,
                _currentCalculation.Tape,
                _currentCalculation.StartProfile,
                _currentCalculation.FProfile,
                _currentCalculation.Penoplex,
                _currentCalculation.Laminatina
            };

            // Меняем кнопку
            BtnIcon.Text = "\uE8FB";  // Fluent Save icon
            BtnText.Text = "Сохранить";
            ChkApplyEconomy.IsChecked = _currentCalculation?.IsProfileEconomyApplied ?? true;
            ChkApplyEconomy.Visibility = Visibility.Collapsed;

            UpdateCalculation();
        }

        /// <summary>
        /// Сбрасывает все поля.
        /// </summary>
        public void Reset()
        {
            _isEditMode = false;
            _editMaterialItem = null;
            _editLaborItem = null;
            TxtHeight.Text = "";
            TxtWidth.Text = "";
            TxtDepth.Text = "";
            TxtDepthHint.Text = "";
            TxtHeightHint.Text = "";
            TxtWidthHint.Text = "";
            NumWindowCount.Value = 1;
            _currentCalculation = null;
            BtnIcon.Text = "\uE710";  // Fluent Add icon
            BtnText.Text = "Добавить в КП";
            ChkApplyEconomy.Visibility = Visibility.Collapsed;
            UpdateCalculation();
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Guard: XAML-парсер может вызвать TextChanged во время InitializeComponent,
            // когда визуальное дерево ещё не полностью создано.
            if (!IsInitialized) return;
            UpdateCalculation();
        }

        /// <summary>
        /// v3.43.3 (review fix #5): обработчик Move-LostFocus для ручного редактирования Кол-во/Цена.
        /// Сработывает ПОСЛЕ коммита WPF binding (UpdateSourceTrigger=LostFocus) —
        /// ставит IsQuantityOverridden=true ТОЛЬКО если ввод реально изменил значение.
        /// Это чище, чем TextChanged: пока юзер печатает потенциально сырой ввод,
        /// override не выставляется — только когда решение принято (ушёл из ячейки).
        /// </summary>
        private void MaterialField_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (_currentCalculation == null) return;

            var text = (tb.Text ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double newVal))
                return; // пусто или невалидно — WPF сам откатил к старому значению

            var m = ResolveSlopeMaterial(tb);
            if (m == null) return;

            var tag = tb.Tag as string;

            if (tag is "Quantity" or "LaborQuantity" or "LaminatinaLaborQuantity")
            {
                if (Math.Abs(m.Quantity - newVal) > 0.0001)
                {
                    m.IsQuantityOverridden = true;
                    m.Quantity = newVal;
                }
            }
            else if (tag is "Price" or "LaborPrice" or "LaminatinaLaborPrice")
            {
                if (Math.Abs(m.Price - newVal) > 0.0001)
                {
                    m.IsQuantityOverridden = true;
                    m.Price = newVal;
                }
            }

            // После коммита — обновим отображение (на случай, если биндинг успел показать «5» а потом свой format показал «5.00»).
            // WPF сам реформатнет на следующий PropertyChanged.
        }

        /// <summary>
        /// v3.43.3: возвращает SlopeMaterial, к которому привязан TextBox.
        /// Для строк из MaterialItems — DataContext сам и есть SlopeMaterial.
        /// Для Труд (отдельная строка ниже) — ищем по x:Name и берём _currentCalculation.Labor.
        /// </summary>
        private SlopeMaterial? ResolveSlopeMaterial(TextBox tb)
        {
            if (tb.DataContext is SlopeMaterial m)
                return m;
            if (tb == TxtLaborQuantity || tb == TxtLaborPrice)
                return _currentCalculation?.Labor;
            if (tb == TxtLaminatinaLaborQuantity || tb == TxtLaminatinaLaborPrice)
                return _currentCalculation?.LaminatinaLabor;
            return null;
        }

        /// <summary>
        /// v3.43.3: обработчик кнопки «Пересчитать». Сбрасывает IsQuantityOverridden
        /// на всех материалах текущего расчёта и принудительно прогоняет UpdateInPlace —
        /// возвращает авто-формулы по текущим W/H/D/Q.
        /// </summary>
        private void BtnRecalculate_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCalculation == null) return;
            _currentCalculation.ResetOverrides();
            UpdateCalculation();  // т.к. _currentCalculation уже существует — внутренний путь выберет UpdateInPlace
            if (TryGetMainWindow(out var mw))
                mw.UpdateTotal();
        }

        /// <summary>
        /// Выполняет пересчёт на основе введённых полей.
        /// v3.43.3: создаёт SlopeCalculation только в первый раз; последующие правки идут
        /// через UpdateInPlace, чтобы сохранить manual override'ы Кол-во/Цена.
        /// </summary>
        private void UpdateCalculation()
        {
            // Guard: XAML-парсер может вызвать TextChanged во время InitializeComponent.
            if (!IsInitialized) return;

            int.TryParse(TxtHeight.Text, out int height);
            int.TryParse(TxtWidth.Text, out int width);
            // v3.43.4: NumericUpDown.Value — int, гарантированно ≥ 1 (min clamp в контроле).
            int windowCount = NumWindowCount.Value;

            string depthInput = TxtDepth.Text?.Trim() ?? "";
            var (depthM, hint) = SlopeCalculatorService.ParseDepth(depthInput);
            TxtDepthHint.Text = hint;

            // Конвертеры мм → м для высоты и ширины (как у глубины)
            TxtHeightHint.Text = height > 0 ? $"{height} мм → {(height / 1000.0):F2} м" : "";
            TxtWidthHint.Text = width > 0 ? $"{width} мм → {(width / 1000.0):F2} м" : "";

            if (height <= 0 || width <= 0 || depthM <= 0)
            {
                _currentCalculation = null;
                TxtLaborSumRow.Text = "0.00";
                TxtAvgWithEconomy.Visibility = Visibility.Collapsed;
                WithoutEconomyRow.Visibility = Visibility.Collapsed;
                TotalAllRow.Visibility = Visibility.Collapsed;
                SummaryCard.Visibility = Visibility.Collapsed;
                MaterialItems.ItemsSource = null;
                ChkApplyEconomy.Visibility = Visibility.Collapsed;
                BtnText.Text = "Добавить в КП";
                OnPropertyChanged(nameof(Labor));
                OnPropertyChanged(nameof(HasCalculation));
                UpdateAddButtonState();
                return;
            }

            // v3.43.5: используем суммарное кол-во откосов в заказе для расчёта экономии
            int totalWindowCount = TotalWindowCountInOrder + windowCount;

            if (_currentCalculation == null)
            {
                // Первый раз: Calculate создаёт новый экземпляр с нуля.
                _currentCalculation = SlopeCalculatorService.Calculate(
                    width, height, depthM,
                    windowCount, totalWindowCount,
                    Prices.Sandwich, Prices.Foam, Prices.Sealant, Prices.Tape,
                    Prices.Start, Prices.FProfile, Prices.Penoplex, Prices.Labor);
                _currentCalculation.IsProfileEconomyApplied = ChkApplyEconomy.IsChecked.GetValueOrDefault(true);
                MaterialItems.ItemsSource = new List<SlopeMaterial>
                {
                    _currentCalculation.Sandwich,
                    _currentCalculation.Foam,
                    _currentCalculation.Sealant,
                    _currentCalculation.Tape,
                    _currentCalculation.StartProfile,
                    _currentCalculation.FProfile,
                    _currentCalculation.Penoplex,
                    _currentCalculation.Laminatina
                };
            }
            else
            {
                // v3.43.7 (bugfix): если чекбокс «Применить экономию» включён,
                // сбрасываем IsQuantityOverridden для sealant/tape перед
                // UpdateInPlace. Иначе предыдущее ручное выключение экономии
                // (которое ставит IsQuantityOverridden=true) блокирует авто-пересчёт
                // в _ApplyDefaults, и sealant/tape остаются per-window (×4),
                // делая realOrderTotal == fullTotal (нет разницы с/без экономии).
                if (ChkApplyEconomy.IsChecked.GetValueOrDefault(true))
                {
                    _currentCalculation.Sealant.IsQuantityOverridden = false;
                    _currentCalculation.Tape.IsQuantityOverridden = false;
                }
                // Последующие правки: in-place, чтобы не сбросить ручные Кол-во/Цена.
                SlopeCalculatorService.UpdateInPlace(
                    _currentCalculation,
                    width, height, depthM,
                    windowCount, totalWindowCount,
                    Prices.Sandwich, Prices.Foam, Prices.Sealant, Prices.Tape,
                    Prices.Start, Prices.FProfile, Prices.Penoplex, Prices.Labor);
                _currentCalculation.IsProfileEconomyApplied = ChkApplyEconomy.IsChecked.GetValueOrDefault(true);
            }

            // v3.44.6 (bugfix): в панели предпросмотра RecalculateSealantAndTape
            // ещё не вызывался, поэтому Start/F-profile оставались per-window.
            // Для корректного отображения сводки с экономией применяем
            // глобальный раскрой сразу (без IsQuantityOverridden, чтобы позже
            // RecalculateSealantAndTape мог пересчитать с учётом других откосов).
            if (_currentCalculation.IsProfileEconomyApplied)
            {
                if (!_currentCalculation.StartProfile.IsQuantityOverridden)
                    _currentCalculation.StartProfile.Quantity = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                        (int)_currentCalculation.WidthMm, (int)_currentCalculation.HeightMm, _currentCalculation.WindowCount);
                if (!_currentCalculation.FProfile.IsQuantityOverridden)
                    _currentCalculation.FProfile.Quantity = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                        (int)_currentCalculation.WidthMm + 100, (int)_currentCalculation.HeightMm + 100, _currentCalculation.WindowCount);
            }

            TxtLaborSumRow.Text = _currentCalculation.TotalLabor.ToString("N2");
            TxtLaminatinaLaborSumRow.Text = _currentCalculation.LaminatinaLabor.Sum.ToString("N2");

            var calc = _currentCalculation;

            // Чекбокс виден всегда, когда экономия ВОЗМОЖНА (N > 1),
            // а не только когда она активна. Иначе при выключении галки
            // чекбокс скрывается навсегда.
            bool potentialEconomy = (TotalWindowCountInOrder + windowCount) > 1;
            ChkApplyEconomy.Visibility = potentialEconomy ? Visibility.Visible : Visibility.Collapsed;

            // ─── ВСЕГО за все откосы ───
            int n = calc.WindowCount;

            // v3.43.6 (fix): calc.GrandTotal уже содержит оптимизированные количества
            // герметика и скотча (sharedSum для всего заказа). Старая формула
            // `calc.GrandTotal * n` умножала shared-материалы на N, «отменяя» экономию
            // и показывая завышенную сумму. Теперь считаем 1-в-1 как OrderItem.Calculations.cs:
            // per-window материалы × N + shared (герметик/скотч) + работа × N.
            double perWindowSum = calc.Sandwich.Sum + calc.Foam.Sum + calc.Penoplex.Sum + calc.Laminatina.Sum;
            if (!calc.IsProfileEconomyApplied)
            {
                perWindowSum += calc.StartProfile.Sum + calc.FProfile.Sum;
            }
            double sharedSum = calc.Sealant.Sum + calc.Tape.Sum;
            if (calc.IsProfileEconomyApplied)
            {
                sharedSum += calc.StartProfile.Sum + calc.FProfile.Sum;
            }
            double realOrderTotal = (perWindowSum * n) + sharedSum + (calc.TotalLabor * n);

            // v3.43.6: сумма БЕЗ экономии — sealant/tape считаются per-window (×N),
            // а не как shared-материалы для всего заказа.
            // v3.44.1: без экономии Старт/F-планка тоже per-window.
            double fullTotal = perWindowSum * n + (calc.Sealant.Price * n) + (calc.Tape.Price * n) + (calc.TotalLabor * n);
            if (calc.IsProfileEconomyApplied)
            {
                int startNoEconQty = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides((int)calc.WidthMm, (int)calc.HeightMm, n);
                int fNoEconQty = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides((int)calc.WidthMm + 100, (int)calc.HeightMm + 100, n);
                fullTotal += (startNoEconQty * calc.StartProfile.Price) + (fNoEconQty * calc.FProfile.Price);
            }

            bool apply = ChkApplyEconomy.IsChecked.GetValueOrDefault(true);
            double orderTotal = apply ? realOrderTotal : fullTotal;
            double totalSavings = Math.Max(0, fullTotal - realOrderTotal);

            // v3.44.7: показываем общую сумму экономии (или возможную экономию,
            // если галка снята). Раньше этот блок был потерян — _ComputeCombinedEconomy
            // существовал, но никогда не вызывался в UpdateCalculation.
            if (n > 1)
            {
                TxtWithoutEconomyLabel.Text = "Без экономии:";
                TxtWithoutEconomy.Text = fullTotal.ToString("N2");
                WithoutEconomyRow.Visibility = Visibility.Visible;

                TxtTotalAllLabel.Text = apply ? "Итого с экономией:" : "Итого:";
                TxtTotalAll.Text = (apply ? realOrderTotal : fullTotal).ToString("N2");
                TotalAllRow.Visibility = Visibility.Visible;

                double avgTotal = apply ? realOrderTotal : fullTotal;
                TxtAvgWithEconomy.Text = $"≈ {(avgTotal / n):N0} ₽/откос";
                TxtAvgWithEconomy.Visibility = Visibility.Visible;

                // Общая экономия. Показываем строку всегда при N > 1,
                // чтобы пользователь видел, что функция работает, даже если
                // для этих размеров экономия = 0.
                bool wasSavingsRowVisible = TotalSavingsRow.Visibility == Visibility.Visible;
                TxtTotalSavingsLabel.Text = apply ? "Экономия:" : "Возможная экономия:";
                TxtTotalSavings.Text = totalSavings > 0 ? $"−{totalSavings:N0} ₽" : "0 ₽";
                Brush savingsBrush = apply && totalSavings > 0
                    ? (TryFindResource("EconomyGreen") as Brush) ?? Brushes.Green
                    : (TryFindResource("TextMuted") as Brush) ?? Brushes.Gray;
                TxtTotalSavings.Foreground = savingsBrush;
                SavingsStarIcon.Foreground = savingsBrush;
                // Предотвращаем однофреймовый всплеск перед анимацией
                if (!wasSavingsRowVisible)
                    TotalSavingsRow.Opacity = 0;
                TotalSavingsRow.Visibility = Visibility.Visible;
                if (!wasSavingsRowVisible)
                    AnimateSavingsRowAppearance();
            }
            else
            {
                TxtTotalAllLabel.Text = "Итого:";
                TxtTotalAll.Text = fullTotal.ToString("N2");
                TotalAllRow.Visibility = Visibility.Visible;
                WithoutEconomyRow.Visibility = Visibility.Collapsed;
                TxtAvgWithEconomy.Visibility = Visibility.Collapsed;
                TotalSavingsRow.Visibility = Visibility.Collapsed;
            }

            // Обновляем текст кнопки с суммой
            UpdateButtonLabel(orderTotal);

            // Сводка расхода материалов
            _BuildMaterialSummary();

            // Обновляем binding для Labor / LaminatinaLabor (Quantity/Price → TextBoxes)
            OnPropertyChanged(nameof(Labor));
            OnPropertyChanged(nameof(LaminatinaLabor));
            OnPropertyChanged(nameof(HasCalculation));
            OnPropertyChanged(nameof(Prices));
            OnPropertyChanged(nameof(SummaryTitle));


            UpdateAddButtonState();
        }

        private void UpdateAddButtonState()
        {
            BtnAddToKp.IsEnabled = _currentCalculation != null;
            BtnAddLaminatina.IsEnabled = _currentCalculation != null;
        }

        /// <summary>
        /// v3.43.6: обновляет текст кнопки с итоговой суммой.
        /// </summary>
        private void UpdateButtonLabel(double total)
        {
            string verb = _isEditMode ? "Сохранить" : "Добавить в КП";
            BtnText.Text = total > 0
                ? $"{verb} — {total:N0} ₽"
                : verb;
        }

        /// <summary>
        /// v3.43.6: переключение чекбокса экономии — пересчитывает отображение
        /// материалов (герметик/скотч показывают per-window или shared количество),
        /// прячет/показывает строки экономии и обновляет сумму на кнопке.
        /// v3.44.1: чекбокс теперь управляет IsProfileEconomyApplied (Старт/F-планка).
        /// v3.44.5: делегирует пересчёт футера/сводки в UpdateCalculation.
        /// </summary>
        private void ChkApplyEconomy_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentCalculation == null) return;
            bool apply = ChkApplyEconomy.IsChecked.GetValueOrDefault(true);
            var calc = _currentCalculation;
            calc.IsProfileEconomyApplied = apply;

            if (apply)
            {
                // Включаем экономию: сбрасываем ручной override и пересчитываем авто-формулы
                calc.Sealant.IsQuantityOverridden = false;
                calc.Tape.IsQuantityOverridden = false;
                calc.StartProfile.IsQuantityOverridden = false;
                calc.FProfile.IsQuantityOverridden = false;
            }
            else
            {
                // Выключаем экономию: sealant/tape и профили = per-window
                calc.Sealant.Quantity = calc.WindowCount;
                calc.Sealant.IsQuantityOverridden = true;
                calc.Sealant.Note = "";
                calc.Tape.Quantity = calc.WindowCount;
                calc.Tape.IsQuantityOverridden = true;
                calc.Tape.Note = "";
                // v3.44.2 (bugfix): без экономии профили — per-window (3 стороны).
                calc.StartProfile.Quantity = SlopeCalculatorService.OptimizeStrips(
                    (int)calc.WidthMm, (int)calc.HeightMm);
                calc.StartProfile.IsQuantityOverridden = true;
                calc.FProfile.Quantity = SlopeCalculatorService.OptimizeStrips(
                    (int)calc.WidthMm + 100, (int)calc.HeightMm + 100);
                calc.FProfile.IsQuantityOverridden = true;
            }

            // Пересчитываем всё через UpdateCalculation — она обновит футер, сводку и кнопку.
            UpdateCalculation();
        }

        /// <summary>
        /// v3.44.9: открывает окно «Детали экономии» с расчётом экономии
        /// по всем активным откосам в текущем заказе.
        /// </summary>
        private void BtnEconomyDetails_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(out var mw)) return;

            // Собираем все активные откосы из заказа.
            var activeSlopes = mw.CalcVM.OrderItems
                .Where(i => i.Name == "Откос" && i.IsActive && i.SlopeData != null)
                .Select(i => i.SlopeData!)
                .Distinct()
                .ToList();

            // Если мы в режиме добавления нового откоса и он ещё не в заказе —
            // включаем его в расчёт, чтобы пользователь видел экономию ДО добавления.
            if (!_isEditMode && _currentCalculation != null)
            {
                activeSlopes.Add(_currentCalculation);
            }

            var window = new SlopeEconomyDetailsWindow
            {
                Owner = Window.GetWindow(this)
            };
            window.LoadData(activeSlopes);
            window.ShowDialog();
        }

        /// <summary>
        /// v3.44.0: добавляет одну ламинатину (материал + работу) к текущему расчёту.
        /// </summary>
        private void BtnAddLaminatina_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCalculation == null) return;

            _currentCalculation.Laminatina.Quantity += 1;
            _currentCalculation.Laminatina.IsQuantityOverridden = true;
            _currentCalculation.LaminatinaLabor.Quantity += 1;
            _currentCalculation.LaminatinaLabor.IsQuantityOverridden = true;

            UpdateCalculation();
        }

        private void BtnAddToKp_Click(object sender, RoutedEventArgs e)
        {
            // v3.44.8 (bugfix): wrap the whole add-to-order flow in try/catch so
            // any unexpected exception is surfaced instead of silently crashing.
            try
            {
                _BtnAddToKp_ClickCore(sender, e);
            }
            catch (Exception ex)
            {
                // Log to a file in AppData for diagnostics, then rethrow so the
                // global App.xaml.cs handler shows the error dialog.
                try
                {
                    string logDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "MosquitoNetCalculator", "logs");
                    Directory.CreateDirectory(logDir);
                    string logPath = Path.Combine(logDir, "slope_add_error.log");
                    File.AppendAllText(logPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BtnAddToKp_Click exception:\n" +
                        $"{ex}\n" +
                        "============================================================\n");
                }
                catch { /* best effort */ }
                throw;
            }
        }

        private void _BtnAddToKp_ClickCore(object sender, RoutedEventArgs e)
        {
            var calc = _currentCalculation;
            if (calc == null) return;

            if (!TryGetMainWindow(out var mw)) return;

            // v3.43.6: если пользователь снял галку «Применить экономию»,
            // клонируем расчёт и ставим sealant/tape per-window (×N) вместо shared.
            SlopeCalculation calcToUse = ChkApplyEconomy.IsChecked.GetValueOrDefault(true)
                ? calc
                : MakeWithoutEconomy(calc);

            if (_isEditMode && _editMaterialItem != null && _editLaborItem != null)
            {
                // ═══════════════════════════════════════════
                // РЕЖИМ РЕДАКТИРОВАНИЯ — обновляем существующие строки
                // ═══════════════════════════════════════════

                // v3.43.5 (review fix): PushUndo перед изменениями — чтобы редактирование
                // откоса можно было откатить через Ctrl+Z.
                mw.PushUndo();

                // Подменяем SlopeData — клон с правками становится новым источником
                _editMaterialItem.SlopeData = calcToUse;
                _editMaterialItem.Price = calcToUse.TotalMaterials;
                _editMaterialItem.Quantity = calcToUse.WindowCount;
                _editMaterialItem.Width = calcToUse.WidthMm;
                _editMaterialItem.Height = calcToUse.HeightMm;

                // Работа не имеет SlopeData (v3.43.4) — обновляем Price/Quantity напрямую
                _editLaborItem.Price = calcToUse.TotalLabor;
                _editLaborItem.Quantity = calcToUse.WindowCount;

                mw.CalcVM.RecalculateAllSlopes();
                mw.UpdateTotal();
                mw.MarkDirty();
                mw.CloseSlopeOverlay();
            }
            else
            {
                // ═══════════════════════════════════════════
                // РЕЖИМ ДОБАВЛЕНИЯ — создаём новые строки
                // ═══════════════════════════════════════════

                // v3.43.4 (bugfix): CalculationViewModel.AddSlope создаёт 2 OrderItem
                // («Откос» + «Работа за откос»), но НЕ подписывает их на
                // RecalculateRequested. QuickAdd уже подписывает AddItem явно в
                // QuickAddControl.AddItem.cs ~line 160; для slope нужно
                // подписаться здесь, иначе тогл IsActive строк откоса в DataGrid
                // вызывает setter модели → RecalculateRequested.Invoke(), но
                // подписчиков нет → MainWindow.UpdateTotal не вызывается →
                // строка не вычитается из общего итога в «Расчёт».
                var newItems = mw.CalcVM.AddSlope(calcToUse, _priceService);
                foreach (var item in newItems)
                    item.RecalculateRequested += mw.RecalculateAndUpdateTotal;

                mw.RecalculateAndUpdateTotal();
                mw.MarkDirty();
                mw.CloseSlopeOverlay();
            }

            Reset();
        }

        /// <summary>
        /// v3.43.6: клонирует расчёт и убирает экономию — sealant/tape
        /// становятся per-window (×WindowCount) вместо shared (ceil(N/4), ceil(N/3)).
        /// v3.44.1: также Старт/F-планка становятся per-window.
        /// Используется когда пользователь снял галку «Применить экономию».
        /// </summary>
        private static SlopeCalculation MakeWithoutEconomy(SlopeCalculation src)
        {
            var clone = src.DeepClone();
            // Ставим per-window количества и флаг IsQuantityOverridden,
            // чтобы RecalculateSealantAndTape() (внутри AddSlope) не перезаписал
            // их обратно на shared ceil(N/4), ceil(N/3).
            clone.IsProfileEconomyApplied = false;
            clone.Sealant.Quantity = clone.WindowCount;
            clone.Sealant.IsQuantityOverridden = true;
            clone.Tape.Quantity = clone.WindowCount;
            clone.Tape.IsQuantityOverridden = true;
            // v3.44.2 (bugfix): Start/F-profile без экономии — per-window (3 стороны),
            // чтобы OrderItem.Total умножал на Quantity (=WindowCount) без двойного учёта.
            clone.StartProfile.Quantity = SlopeCalculatorService.OptimizeStrips(
                (int)clone.WidthMm, (int)clone.HeightMm);
            clone.StartProfile.IsQuantityOverridden = true;
            clone.FProfile.Quantity = SlopeCalculatorService.OptimizeStrips(
                (int)clone.WidthMm + 100, (int)clone.HeightMm + 100);
            clone.FProfile.IsQuantityOverridden = true;
            return clone;
        }

        /// <summary>
        /// v3.44.7: чистая функция для расчёта общей экономии.
        /// Используется в юнит-тестах и может быть использована для tooltip'ов.
        /// </summary>
        internal static double ComputeTotalSavings(double fullTotal, double realOrderTotal)
            => Math.Max(0, fullTotal - realOrderTotal);

        /// <summary>
        /// v3.43.5: строит сводку расхода материалов на все откосы.
        /// Показывает per-window количество, итоговое (×N), и экономию для герметика/скотча/старта/F-планки.
        /// </summary>
        private void _BuildMaterialSummary()
        {
            if (_currentCalculation == null)
            {
                SummaryCard.Visibility = Visibility.Collapsed;
                return;
            }

            var rows = BuildMaterialSummaryRows(_currentCalculation);
            SummaryItems.ItemsSource = rows;

            // v3.44.6: если экономия включена, но для этих размеров раскрой
            // не даёт сбережений, показываем поясняющую подсказку.
            bool economyEnabled = _currentCalculation.IsProfileEconomyApplied;
            bool hasAnySavings = rows.Any(r => r.HasNote);
            if (TxtNoSavingsHint != null)
            {
                TxtNoSavingsHint.Visibility = economyEnabled && !hasAnySavings
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            SummaryCard.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// v3.44.4: чистая функция построения сводки материалов.
        /// Вынесена из <see cref="_BuildMaterialSummary"/> для юнит-тестирования
        /// без необходимости создавать WPF-контрол и STA-поток.
        /// </summary>
        internal static List<MaterialSummaryRow> BuildMaterialSummaryRows(SlopeCalculation calc)
        {
            int n = calc.WindowCount;
            var rows = new List<MaterialSummaryRow>();

            // Сэндвич
            double sandwichQty = calc.Sandwich.Quantity;
            rows.Add(new MaterialSummaryRow
            {
                Name = "Сэндвич",
                PerDetail = $"{sandwichQty:F3} м² ×{n}",
                TotalDisplay = $"{sandwichQty * n:F3} м²",
            });

            // Пена
            rows.Add(new MaterialSummaryRow
            {
                Name = "Пена",
                PerDetail = $"1 баллон ×{n}",
                TotalDisplay = $"{n} баллон{(n == 1 ? "" : "ов")}",
            });

            // Герметик (с экономией)
            double sealantQty = calc.Sealant.Quantity;
            double sealantWas = 1.0 * n;
            int sealantSaved = (int)(sealantWas - sealantQty);
            double sealantSavings = sealantSaved * calc.Sealant.Price;
            string sealantNote = sealantSaved > 0 ? $"экон. {sealantWas:F0} → {sealantQty:F0} тюб. = −{sealantSavings:N0} ₽" : "";
            string? sealantTooltip = sealantSaved > 0
                ? $"Экономия за счёт общего расхода герметика на все окна.\n"
                  + $"Было: {sealantWas:F0} тюб. × {calc.Sealant.Price:N0} ₽ = {sealantWas * calc.Sealant.Price:N0} ₽\n"
                  + $"Стало: {sealantQty:F0} тюб. × {calc.Sealant.Price:N0} ₽ = {sealantQty * calc.Sealant.Price:N0} ₽\n"
                  + $"Экономия: {sealantSaved} тюб. × {calc.Sealant.Price:N0} ₽ = −{sealantSavings:N0} ₽"
                : null;
            rows.Add(new MaterialSummaryRow
            {
                Name = "Герметик",
                PerDetail = $"{sealantQty:F0} тюбик{(sealantQty == 1 ? "" : "а")}",
                TotalDisplay = $"{sealantQty:F0} тюбик{(sealantQty == 1 ? "" : "а")}",
                Note = sealantNote,
                EconomyTooltip = sealantTooltip,
            });

            // Скотч (с экономией)
            double tapeQty = calc.Tape.Quantity;
            double tapeWas = 1.0 * n;
            int tapeSaved = (int)(tapeWas - tapeQty);
            double tapeSavings = tapeSaved * calc.Tape.Price;
            string tapeNote = tapeSaved > 0 ? $"экон. {tapeWas:F0} → {tapeQty:F0} мот. = −{tapeSavings:N0} ₽" : "";
            string? tapeTooltip = tapeSaved > 0
                ? $"Экономия за счёт общего расхода скотча на все окна.\n"
                  + $"Было: {tapeWas:F0} мот. × {calc.Tape.Price:N0} ₽ = {tapeWas * calc.Tape.Price:N0} ₽\n"
                  + $"Стало: {tapeQty:F0} мот. × {calc.Tape.Price:N0} ₽ = {tapeQty * calc.Tape.Price:N0} ₽\n"
                  + $"Экономия: {tapeSaved} мот. × {calc.Tape.Price:N0} ₽ = −{tapeSavings:N0} ₽"
                : null;
            rows.Add(new MaterialSummaryRow
            {
                Name = "Скотч",
                PerDetail = $"{tapeQty:F0} моток",
                TotalDisplay = $"{tapeQty:F0} моток",
                Note = tapeNote,
                EconomyTooltip = tapeTooltip,
            });

            // Старт (с экономией по раскрою на все окна — 3 стороны)
            // v3.44.9: PerDetail и TotalDisplay теперь показывают фактическое
            // общее количество полос, как у герметика/скотча.
            int startNoEcon = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                (int)calc.WidthMm, (int)calc.HeightMm, n);
            int startQtyTotal = calc.IsProfileEconomyApplied
                ? (int)calc.StartProfile.Quantity // global total when economy is on
                : (int)calc.StartProfile.Quantity * n;
            int startSaved = Math.Max(0, startNoEcon - startQtyTotal);
            double startSavings = startSaved * calc.StartProfile.Price;
            string startNote = startSaved > 0 ? $"экон. {startNoEcon:F0} → {startQtyTotal:F0} пол. = −{startSavings:N0} ₽" : "";
            string? startTooltip = startSaved > 0
                ? $"Экономия за счёт общего раскроя профилей на все окна.\n"
                  + $"Было: {startNoEcon:F0} пол. × {calc.StartProfile.Price:N0} ₽ = {startNoEcon * calc.StartProfile.Price:N0} ₽\n"
                  + $"Стало: {startQtyTotal:F0} пол. × {calc.StartProfile.Price:N0} ₽ = {startQtyTotal * calc.StartProfile.Price:N0} ₽\n"
                  + $"Экономия: {startSaved} пол. × {calc.StartProfile.Price:N0} ₽ = −{startSavings:N0} ₽"
                : null;
            rows.Add(new MaterialSummaryRow
            {
                Name = "Старт",
                PerDetail = $"{startQtyTotal:F0} пол.",
                TotalDisplay = $"{startQtyTotal:F0} пол. (3 м)",
                Note = startNote,
                EconomyTooltip = startTooltip,
            });

            // F-планка (с экономией по раскрою на все окна — 3 стороны +100 мм)
            // v3.44.9: PerDetail и TotalDisplay теперь показывают фактическое
            // общее количество полос, как у герметика/скотча.
            int fNoEcon = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                (int)calc.WidthMm + 100, (int)calc.HeightMm + 100, n);
            int fQtyTotal = calc.IsProfileEconomyApplied
                ? (int)calc.FProfile.Quantity // global total when economy is on
                : (int)calc.FProfile.Quantity * n;
            int fSaved = Math.Max(0, fNoEcon - fQtyTotal);
            double fSavings = fSaved * calc.FProfile.Price;
            string fNote = fSaved > 0 ? $"экон. {fNoEcon:F0} → {fQtyTotal:F0} пол. = −{fSavings:N0} ₽" : "";
            string? fTooltip = fSaved > 0
                ? $"Экономия за счёт общего раскроя профилей на все окна.\n"
                  + $"Было: {fNoEcon:F0} пол. × {calc.FProfile.Price:N0} ₽ = {fNoEcon * calc.FProfile.Price:N0} ₽\n"
                  + $"Стало: {fQtyTotal:F0} пол. × {calc.FProfile.Price:N0} ₽ = {fQtyTotal * calc.FProfile.Price:N0} ₽\n"
                  + $"Экономия: {fSaved} пол. × {calc.FProfile.Price:N0} ₽ = −{fSavings:N0} ₽"
                : null;
            rows.Add(new MaterialSummaryRow
            {
                Name = "F-планка",
                PerDetail = $"{fQtyTotal:F0} пол.",
                TotalDisplay = $"{fQtyTotal:F0} пол. (3 м)",
                Note = fNote,
                EconomyTooltip = fTooltip,
            });

            // Пеноплекс
            double penoplexQty = calc.Penoplex.Quantity;
            rows.Add(new MaterialSummaryRow
            {
                Name = "Пеноплекс",
                PerDetail = $"{penoplexQty:F0} лист{(penoplexQty == 1 ? "" : "а")} ×{n}",
                TotalDisplay = $"{penoplexQty * n:F0} лист{(penoplexQty * n == 1 ? "" : "ов")}",
            });

            // Работа
            double laborQty = calc.Labor.Quantity;
            rows.Add(new MaterialSummaryRow
            {
                Name = "Работа",
                PerDetail = $"{laborQty:F2} м.п. ×{n}",
                TotalDisplay = $"{laborQty * n:F2} м.п.",
            });

            // Ламинат (показываем только если > 0)
            double laminatinaQty = calc.Laminatina.Quantity;
            if (laminatinaQty > 0)
            {
                rows.Add(new MaterialSummaryRow
                {
                    Name = "Ламинат",
                    PerDetail = $"{laminatinaQty:F0} шт. ×{n}",
                    TotalDisplay = $"{laminatinaQty * n:F0} шт.",
                });

                double laminatinaLaborQty = calc.LaminatinaLabor.Quantity;
                rows.Add(new MaterialSummaryRow
                {
                    Name = "Работа за ламинат",
                    PerDetail = $"{laminatinaLaborQty:F0} шт. ×{n}",
                    TotalDisplay = $"{laminatinaLaborQty * n:F0} шт.",
                });
            }

            return rows;
        }

        /// <summary>
        /// Русское склонение: 1 откос, 2 откоса, 5 откосов.
        /// </summary>
        private static string GetRussianPlural(int n)
        {
            int m100 = n % 100;
            int m10 = n % 10;
            if (m100 >= 11 && m100 <= 14) return "ов";
            return m10 == 1 ? "" : (m10 >= 2 && m10 <= 4 ? "а" : "ов");
        }

        private bool TryGetMainWindow([NotNullWhen(true)] out MainWindow? mw)
        {
            mw = Window.GetWindow(this) as MainWindow;
            return mw != null;
        }

        /// <summary>
        /// v3.44.8: анимация появления строки общей экономии.
        /// Срабатывает только при переходе из Collapsed в Visible.
        /// </summary>
        private void AnimateSavingsRowAppearance()
        {
            if (TotalSavingsRow.Resources["SavingsRowAppearStoryboard"] is not Storyboard storyboard)
                return;

            // Сбрасываем начальное состояние для повторного проигрывания
            if (TotalSavingsRow.RenderTransform is TranslateTransform transform)
                transform.Y = 8;
            TotalSavingsRow.Opacity = 0;

            storyboard.Begin(TotalSavingsRow);
        }


    }
}
