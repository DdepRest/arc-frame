using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// v3.43.5: строка сводки расхода материалов (отображается в Card 3).
    /// </summary>
    public class MaterialSummaryRow
    {
        public string Name { get; set; } = "";
        /// <summary>Детализация per-window: "1.545 м²" или "×3"</summary>
        public string PerDetail { get; set; } = "";
        /// <summary>Итоговое количество: "4.635 м²"</summary>
        public string TotalDisplay { get; set; } = "";
        /// <summary>Зелёный чип экономии: "экон. 2 = -700"</summary>
        public string Note { get; set; } = "";
        public bool HasNote => !string.IsNullOrEmpty(Note);
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
        /// v3.43.6: подпись экономии — герметик и скотч (shared-материалы).
        /// v3.44.1: добавлены Старт/F-планка, если включена IsProfileEconomyApplied.
        /// </summary>
        public string EconomyLabel
        {
            get
            {
                if (_currentCalculation == null) return "Экономия:";
                var parts = new List<string>();
                double sealantSavings = _currentCalculation.WindowCount * _currentCalculation.Sealant.Price
                                        - _currentCalculation.Sealant.Quantity * _currentCalculation.Sealant.Price;
                if (sealantSavings > 0) parts.Add("герметик");
                double tapeSavings = _currentCalculation.WindowCount * _currentCalculation.Tape.Price
                                     - _currentCalculation.Tape.Quantity * _currentCalculation.Tape.Price;
                if (tapeSavings > 0) parts.Add("скотч");

                if (_currentCalculation.IsProfileEconomyApplied)
                {
                    int startNoEcon = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                        (int)_currentCalculation.WidthMm, (int)_currentCalculation.HeightMm, _currentCalculation.WindowCount);
                    int fNoEcon = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                        (int)_currentCalculation.WidthMm + 100, (int)_currentCalculation.HeightMm + 100, _currentCalculation.WindowCount);
                    double startSavings = Math.Max(0, (startNoEcon - _currentCalculation.StartProfile.Quantity) * _currentCalculation.StartProfile.Price);
                    double fSavings = Math.Max(0, (fNoEcon - _currentCalculation.FProfile.Quantity) * _currentCalculation.FProfile.Price);
                    if (startSavings > 0) parts.Add("старт");
                    if (fSavings > 0) parts.Add("F-планка");
                }

                if (parts.Count == 0) return "Экономия:";
                return "Экономия: " + string.Join(", ", parts);
            }
        }

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
            _currentCalculation = OrderItem.DeepCloneSlopeData(sd);

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
                TxtTotalMaterials.Text = "0.00";
                TxtLaborSumRow.Text = "0.00";
                TxtLaborSumTotals.Text = "0.00";
                TxtGrandTotal.Text = "0.00";
                TxtAvgWithEconomy.Visibility = Visibility.Collapsed;
                EconomyRow.Visibility = Visibility.Collapsed;
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

            // Ставим Note для герметика/скотча — показываем оригинальное количество без экономии
            _currentCalculation.Sealant.Note = _currentCalculation.Sealant.Quantity < windowCount
                ? $"было {windowCount}"
                : "";
            _currentCalculation.Tape.Note = _currentCalculation.Tape.Quantity < windowCount
                ? $"было {windowCount}"
                : "";

            TxtTotalMaterials.Text = _currentCalculation.TotalMaterials.ToString("N2");
            TxtLaborSumRow.Text = _currentCalculation.TotalLabor.ToString("N2");
            TxtLaborSumTotals.Text = _currentCalculation.TotalLabor.ToString("N2");
            TxtLaminatinaLaborSumRow.Text = _currentCalculation.LaminatinaLabor.Sum.ToString("N2");

            // ─── ВСЕГО за откос (per-window, без shared-материалов) ───
            // v3.43.7 (bugfix): GrandTotal включал SHARED sealant/tape суммы
            // (1 тюбик + 2 мотка на ВСЕ N окон), из-за чего per-window стоимость
            // странно росла при увеличении N (9524 → 9659). Теперь считаем
            // честную per-window стоимость: каждый откос получает свой тюбик
            // герметика и моток скотча (без учёта экономии).
            var calc = _currentCalculation;
            double perWindowSum = calc.Sandwich.Sum + calc.Foam.Sum + calc.Penoplex.Sum + calc.Laminatina.Sum;
            if (!calc.IsProfileEconomyApplied)
            {
                perWindowSum += calc.StartProfile.Sum + calc.FProfile.Sum;
            }
            double perWindowGrandTotal = perWindowSum + calc.Sealant.Price + calc.Tape.Price + calc.TotalLabor;
            TxtGrandTotal.Text = perWindowGrandTotal.ToString("N2");

            // ─── Экономия (комбинированная: герметик + скотч) ───
            double totalSavings = _ComputeCombinedEconomy(calc);
            bool hasEconomy = totalSavings > 0;
            if (hasEconomy)
            {
                TxtEconomySavings.Text = $"-{totalSavings:N2}";
                EconomyRow.Visibility = Visibility.Visible;
            }
            else
            {
                EconomyRow.Visibility = Visibility.Collapsed;
            }

            // Чекбокс виден всегда, когда экономия ВОЗМОЖНА (N > 1),
            // а не только когда она активна (hasEconomy). Иначе при
            // выключении галки hasEconomy=0 → чекбокс скрывается навсегда.
            bool potentialEconomy = (TotalWindowCountInOrder + windowCount) > 1;
            ChkApplyEconomy.Visibility = potentialEconomy ? Visibility.Visible : Visibility.Collapsed;

            // Если чекбокс выключен — скрываем EconomyRow (TotalAllRow гейтится ниже)
            if (!ChkApplyEconomy.IsChecked.GetValueOrDefault(true))
                EconomyRow.Visibility = Visibility.Collapsed;

            // ─── ВСЕГО за все откосы ───
            int n = calc.WindowCount;

            // v3.43.6 (fix): calc.GrandTotal уже содержит оптимизированные количества
            // герметика и скотча (sharedSum для всего заказа). Старая формула
            // `calc.GrandTotal * n` умножала shared-материалы на N, «отменяя» экономию
            // и показывая завышенную сумму. Теперь считаем 1-в-1 как OrderItem.Calculations.cs:
            // per-window материалы × N + shared (герметик/скотч) + работа × N.
            perWindowSum = calc.Sandwich.Sum + calc.Foam.Sum + calc.Penoplex.Sum + calc.Laminatina.Sum;
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

            // Итог, который пойдёт в заказ (с учётом чекбокса экономии)
            double orderTotal = ChkApplyEconomy.IsChecked.GetValueOrDefault(true) ? realOrderTotal : fullTotal;

            if (n > 1)
            {
                TxtWithoutEconomyLabel.Text = $"Итого за {n} откос{GetRussianPlural(n)} (без экономии):";
                TxtWithoutEconomy.Text = fullTotal.ToString("N2");
                WithoutEconomyRow.Visibility = Visibility.Visible;

                TxtTotalAllLabel.Text = $"Итого с экономией за {n} откос{GetRussianPlural(n)}:";
                TxtTotalAll.Text = realOrderTotal.ToString("N2");
                TotalAllRow.Visibility = ChkApplyEconomy.IsChecked.GetValueOrDefault(true)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                // Средняя стоимость откоса с экономией
                if (ChkApplyEconomy.IsChecked.GetValueOrDefault(true))
                {
                    TxtAvgWithEconomy.Text = $"≈ {(realOrderTotal / n):N0} ₽/откос с экономией";
                    TxtAvgWithEconomy.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtAvgWithEconomy.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                WithoutEconomyRow.Visibility = Visibility.Collapsed;
                TotalAllRow.Visibility = Visibility.Collapsed;
                TxtAvgWithEconomy.Visibility = Visibility.Collapsed;
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
            OnPropertyChanged(nameof(EconomyLabel));

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
                int totalWc = TotalWindowCountInOrder + calc.WindowCount;
                SlopeCalculatorService.UpdateInPlace(calc,
                    (int)calc.WidthMm, (int)calc.HeightMm, calc.DepthM,
                    calc.WindowCount, totalWc,
                    Prices.Sandwich, Prices.Foam, Prices.Sealant, Prices.Tape,
                    Prices.Start, Prices.FProfile, Prices.Penoplex, Prices.Labor);
                // Обновляем чипы «было N»
                calc.Sealant.Note = calc.Sealant.Quantity < calc.WindowCount
                    ? $"было {calc.WindowCount}" : "";
                calc.Tape.Note = calc.Tape.Quantity < calc.WindowCount
                    ? $"было {calc.WindowCount}" : "";
            }
            else
            {
                // Выключаем экономию: sealant/tape и профили = per-window (×WindowCount)
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

            // Обновляем итоги в футере
            TxtTotalMaterials.Text = calc.TotalMaterials.ToString("N2");
            double perWinSum = calc.Sandwich.Sum + calc.Foam.Sum + calc.Penoplex.Sum + calc.Laminatina.Sum;
            if (!calc.IsProfileEconomyApplied)
            {
                perWinSum += calc.StartProfile.Sum + calc.FProfile.Sum;
            }
            TxtGrandTotal.Text = (perWinSum + calc.Sealant.Price + calc.Tape.Price + calc.TotalLabor).ToString("N2");
            TxtLaborSumRow.Text = calc.TotalLabor.ToString("N2");
            TxtLaborSumTotals.Text = calc.TotalLabor.ToString("N2");
            TxtLaminatinaLaborSumRow.Text = calc.LaminatinaLabor.Sum.ToString("N2");

            EconomyRow.Visibility = _ComputeCombinedEconomy(calc) > 0
                ? Visibility.Visible : Visibility.Collapsed;
            if (calc.WindowCount > 1 && apply)
                TotalAllRow.Visibility = Visibility.Visible;
            else
                TotalAllRow.Visibility = Visibility.Collapsed;

            // Пересчитываем сумму на кнопке
            int n = calc.WindowCount;
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
            double fullTotal = perWindowSum * n + (calc.Sealant.Price * n) + (calc.Tape.Price * n) + (calc.TotalLabor * n);
            if (calc.IsProfileEconomyApplied)
            {
                int startNoEconQty = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides((int)calc.WidthMm, (int)calc.HeightMm, n);
                int fNoEconQty = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides((int)calc.WidthMm + 100, (int)calc.HeightMm + 100, n);
                fullTotal += (startNoEconQty * calc.StartProfile.Price) + (fNoEconQty * calc.FProfile.Price);
            }
            double realOrderTotal = (perWindowSum * n) + sharedSum + (calc.TotalLabor * n);
            double orderTotal = apply ? realOrderTotal : fullTotal;
            UpdateButtonLabel(orderTotal);

            // Средняя стоимость откоса с экономией
            if (n > 1 && apply)
            {
                TxtAvgWithEconomy.Text = $"≈ {(realOrderTotal / n):N0} ₽/откос с экономией";
                TxtAvgWithEconomy.Visibility = Visibility.Visible;
            }
            else
            {
                TxtAvgWithEconomy.Visibility = Visibility.Collapsed;
            }

            // Обновляем сводку
            _BuildMaterialSummary();
            OnPropertyChanged(nameof(EconomyLabel));
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
            var clone = OrderItem.DeepCloneSlopeData(src);
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
        /// v3.43.6: вычисляет экономию по герметику и скотчу (shared-материалы,
        /// количество которых считается по N_total, а не per-window).
        /// v3.44.1: добавлена экономия по Старт/F-планке, если включена
        /// IsProfileEconomyApplied. Без экономии профили считаются per-window.
        /// </summary>
        private double _ComputeCombinedEconomy(SlopeCalculation calc)
        {
            double sealantSavings = Math.Max(0,
                calc.WindowCount * calc.Sealant.Price - calc.Sealant.Quantity * calc.Sealant.Price);
            double tapeSavings = Math.Max(0,
                calc.WindowCount * calc.Tape.Price - calc.Tape.Quantity * calc.Tape.Price);

            double startSavings = 0;
            double fSavings = 0;
            if (calc.IsProfileEconomyApplied)
            {
                int startNoEconQty = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                    (int)calc.WidthMm, (int)calc.HeightMm, calc.WindowCount);
                int fNoEconQty = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                    (int)calc.WidthMm + 100, (int)calc.HeightMm + 100, calc.WindowCount);
                startSavings = Math.Max(0, (startNoEconQty - calc.StartProfile.Quantity) * calc.StartProfile.Price);
                fSavings = Math.Max(0, (fNoEconQty - calc.FProfile.Quantity) * calc.FProfile.Price);
            }

            return sealantSavings + tapeSavings + startSavings + fSavings;
        }

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
            double sealantWithout = 1.0 * n;
            int sealantSaved = (int)(sealantWithout - sealantQty);
            rows.Add(new MaterialSummaryRow
            {
                Name = "Герметик",
                PerDetail = $"{sealantQty:F0} тюбик{(sealantQty == 1 ? "" : "а")}",
                TotalDisplay = $"{sealantQty:F0} тюбик{(sealantQty == 1 ? "" : "а")}",
                Note = sealantSaved > 0
                    ? $"экон. {sealantSaved} × {calc.Sealant.Price:N0} = -{sealantSaved * calc.Sealant.Price:N0}₽"
                    : "",
            });

            // Скотч (с экономией)
            double tapeQty = calc.Tape.Quantity;
            double tapeWithout = 1.0 * n;
            int tapeSaved = (int)(tapeWithout - tapeQty);
            rows.Add(new MaterialSummaryRow
            {
                Name = "Скотч",
                PerDetail = $"{tapeQty:F0} моток",
                TotalDisplay = $"{tapeQty:F0} моток",
                Note = tapeSaved > 0
                    ? $"экон. {tapeSaved} × {calc.Tape.Price:N0} = -{tapeSaved * calc.Tape.Price:N0}₽"
                    : "",
            });

            // Старт (с экономией по раскрою на все окна — 3 стороны)
            // v3.44.2 (bugfix): StartProfile.Quantity — per-window (3 стороны) всегда.
            // TotalDisplay показывает общее количество для n окон.
            // v3.44.4 (bugfix): PerDetail должен показывать per-window количество,
            // а TotalDisplay — фактическое общее количество (с учётом экономии).
            int startNoEcon = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                (int)calc.WidthMm, (int)calc.HeightMm, n);
            double startQtyPerWindow = calc.IsProfileEconomyApplied
                ? SlopeCalculatorService.OptimizeStrips((int)calc.WidthMm, (int)calc.HeightMm)
                : calc.StartProfile.Quantity;
            int startQtyTotal = calc.IsProfileEconomyApplied
                ? (int)calc.StartProfile.Quantity // global total when economy is on
                : (int)startQtyPerWindow * n;
            int startSaved = Math.Max(0, startNoEcon - startQtyTotal);
            rows.Add(new MaterialSummaryRow
            {
                Name = "Старт",
                PerDetail = $"{startQtyPerWindow:F0} пол. ×{n}",
                TotalDisplay = $"{startQtyTotal:F0} полос{(startQtyTotal == 1 ? "" : " 3м")}",
                Note = startSaved > 0
                    ? $"экон. {startSaved} × {calc.StartProfile.Price:N0} = -{startSaved * calc.StartProfile.Price:N0}₽"
                    : "",
            });

            // F-планка (с экономией по раскрою на все окна — 3 стороны +100 мм)
            int fNoEcon = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                (int)calc.WidthMm + 100, (int)calc.HeightMm + 100, n);
            double fQtyPerWindow = calc.IsProfileEconomyApplied
                ? SlopeCalculatorService.OptimizeStrips((int)calc.WidthMm + 100, (int)calc.HeightMm + 100)
                : calc.FProfile.Quantity;
            int fQtyTotal = calc.IsProfileEconomyApplied
                ? (int)calc.FProfile.Quantity // global total when economy is on
                : (int)fQtyPerWindow * n;
            int fSaved = Math.Max(0, fNoEcon - fQtyTotal);
            rows.Add(new MaterialSummaryRow
            {
                Name = "F-планка",
                PerDetail = $"{fQtyPerWindow:F0} пол. ×{n}",
                TotalDisplay = $"{fQtyTotal:F0} полос{(fQtyTotal == 1 ? "" : " 3м")}",
                Note = fSaved > 0
                    ? $"экон. {fSaved} × {calc.FProfile.Price:N0} = -{fSaved * calc.FProfile.Price:N0}₽"
                    : "",
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


    }
}
