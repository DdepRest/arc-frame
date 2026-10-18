using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows.Media.Imaging;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

using PageMode = MosquitoNetCalculator.Models.PageMode;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Встроенный контрол предпросмотра КП с настройками печати.
    /// Используется как в PrintPreviewWindow (отдельное окно), так и
    /// в PrintOverlay главного окна.
    /// </summary>
    public partial class PrintPreviewControl : UserControl
    {
        private FlowDocument _document;           // original (A4) — для печати
        private FlowDocument? _previewDocument;     // deep-clone — для предпросмотра
        // Порядок предпросмотра повторяет печать/PDF: страницы 1.._cleanPageCount —
        // чистый экземпляр КП, дальше (при включённом флажке) — блок «В производство».
        private int _cleanPageCount;                // страниц чистого экземпляра в предпросмотре
        private int _productionStartPage;           // 1-based старт блока «В производство» (0 = блока нет)
        private FlowDocument? _previewBuiltFrom;    // источник последней сборки предпросмотра
        private bool _previewBuiltWithProduction;   // собран ли предпросмотр с блоком «В производство»
        private PrintService _printService;
        private string _contractNumber;
        private PrintSettings _settings;
        private readonly List<PrintQueue> _printerQueues = new();

        // ── PDF export data ──
        private List<OrderItem> _items = new();
        private ClientInfo _clientInfo = new();
        private double _totalAmount;
        private string _amountInWords = string.Empty;

        private FlowDocumentPageViewer? _innerViewer;
        private DependencyPropertyDescriptor? _pageNumberDescriptor;
        // PageCount валидируется позже MasterPageNumber при замене Document —
        // держим отдельный descriptor, чтобы индикатор/кнопки обновились сразу,
        // а не после следующего клика (тот же колбэк OnPageNumberChanged).
        private DependencyPropertyDescriptor? _pageCountDescriptor;
        private readonly DispatcherTimer _textChangedDebounce = new()
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };

        // ── Zoom ──
        private const double ZoomStep = 0.1;
        private const double ZoomDefault = 1.0;
        private const double ZoomMin = 0.5;
        private const double ZoomMax = 4.0;
        private double _currentZoom = ZoomDefault;
        private double _fitZoom = ZoomDefault;
        private bool _isUserZoom = false;
        private readonly DispatcherTimer _resizeDebounce = new()
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };

        /// <summary>Событие закрытия — родитель (окно или overlay) скрывает контрол.</summary>
        public event EventHandler? Closed;

        public PrintPreviewControl()
        {
            InitializeComponent();
            _document = new FlowDocument();   // placeholder — будет заменён в Initialize()
            _printService = new PrintService();
            _contractNumber = "";
            _settings = new PrintSettings();

            _textChangedDebounce.Tick += (_, _) =>
            {
                _textChangedDebounce.Stop();
                UpdateDimmingOverlay();
            };

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            this.SizeChanged += OnControlSizeChanged;
            _resizeDebounce.Tick += (_, _) =>
            {
                _resizeDebounce.Stop();
                if (!IsLoaded || PreviewScroller == null) return;
                if (PreviewScroller.ViewportWidth > 0)
                    FitToViewport();
            };
        }

        /// <summary>
        /// Инициализирует контрол документом и настройками.
        /// Вызывается перед показом — как в окне, так и в overlay.
        /// </summary>
        public void Initialize(
            FlowDocument document,
            PrintService printService,
            string contractNumber,
            PrintSettings? savedSettings,
            List<OrderItem>? items = null,
            ClientInfo? clientInfo = null,
            double totalAmount = 0,
            string? amountInWords = null)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _printService = printService ?? throw new ArgumentNullException(nameof(printService));
            _contractNumber = contractNumber ?? "";
            _settings = (savedSettings ?? new PrintSettings()).Clone();

            _items = items ?? new List<OrderItem>();
            _clientInfo = clientInfo ?? new ClientInfo();
            _totalAmount = totalAmount;
            _amountInWords = amountInWords ?? string.Empty;

            // Панель обязана читать СВОИ контролы из настроек при КАЖДОМ открытии,
            // а не только в OnLoaded: контрол живёт в оверлее постоянно
            // (Visibility переключается — Loaded повторно не поднимается),
            // поэтому RestoreSettings() из OnLoaded при повторном открытии не
            // выполнялся. Результат — рассинхрон: документ собирался по
            // _settings.IncludeProductionCopy (персистентная галочка), а флажок в
            // панели оставался выключенным: штамп не показывался нигде, а «Печать»
            // и «Сохранить PDF», собрав настройки по флажку, выводили один+
            // экземпляр вместо показанных двух.
            RestoreSettings();

            // Сборка по ТЕМ ЖЕ настройкам уже могла произойти внутри Checked-события
            // RestoreSettings (см. RebuildPreviewDocument/alreadyBuilt) — повторный
            // вызов здесь ничего не пересобирает, только донастраивает overlays.
            RebuildPreviewDocument(_settings.IncludeProductionCopy);

            // Enable PDF save only if we have export data
            SavePdfButton.IsEnabled = _items.Count > 0;
            if (_items.Count == 0)
                SavePdfButton.ToolTip = "Добавьте позиции в заказ для экспорта в PDF";
            else
                SavePdfButton.ToolTip = null;
        }

        /// <summary>Возвращает текущие настройки печати.</summary>
        public PrintSettings GetSettings()
        {
            return _settings.Clone();
        }

        /// <summary>
        /// v3.50 split-button «Печать КП»: программный клик по кнопке экспорта PDF.
        /// Публичный мост для ActionBar — переиспользует ВЕСЬ существующий путь
        /// SavePdf_Click (диалог сохранения, настройки, QuestPDF), без дублирования.
        /// </summary>
        public void TriggerPdfExport() => SavePdf_Click(this, new RoutedEventArgs());

        /// <summary>
        /// v3.50 split-button «Печать КП»: программный вызов существующей печати
        /// на принтер (Print_Click) — без дублирования диалогов и настроек.
        /// </summary>
        public void TriggerPrinterPrint() => Print_Click(this, new RoutedEventArgs());

        /// <summary>
        /// v3.50.3 split-button «Печать КП»: прямая печать «Чистая» + «В производство» —
        /// принудительно включает производственную копию (как если бы пользователь
        /// поставил флажок в предпросмотре) и запускает существующую печать на
        /// принтер (Print_Click). Никакого второго печатного пайплайна.
        /// </summary>
        public void TriggerPrinterPrintWithProductionCopy()
        {
            ProductionCopyCheck.IsChecked = true; // CollectSettingsInto читает флажок
            TriggerPrinterPrint();
        }

        /// <summary>
        /// Собирает текущие значения UI в объект настроек.
        /// Используется перед сохранением, чтобы ESC/закрытие оверлея
        /// не теряли изменения, внесённые пользователем после открытия.
        /// </summary>
        public void CollectSettings()
        {
            CollectSettingsInto(_settings);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Deep clone
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Собирает документ предпросмотра в порядке РЕАЛЬНОГО вывода:
        /// сначала чистый экземпляр КП, затем — отдельным блоком после него —
        /// копия «В производство». Раньше штамп накладывался поверх ПЕРВОЙ страницы
        /// чистого КП, и в предпросмотре «В ПРОИЗВОДСТВО» выглядело идущим первым,
        /// хотя печать и PDF всегда кладут его вторым.
        /// </summary>
        /// <param name="includeProductionCopy">Флажок «Добавить копию “В производство”».</param>
        /// <param name="cleanPageCount">Число страниц чистого экземпляра КП.</param>
        /// <param name="productionStartPage">1-based номер первого листа производственного
        /// блока (0 = блока нет — штамп не показывается).</param>
        internal static FlowDocument BuildPreviewDocument(
            FlowDocument source,
            bool includeProductionCopy,
            out int cleanPageCount,
            out int productionStartPage,
            Func<FlowDocument?>? documentFactory = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var clean = DeepCloneDocument(source);
            if (ReferenceEquals(clean, source) && documentFactory != null)
            {
                var recreated = documentFactory();
                if (recreated != null)
                    clean = recreated;
            }

            cleanPageCount = CountPages(clean);
            productionStartPage = 0;

            if (includeProductionCopy)
            {
                productionStartPage = cleanPageCount > 0 ? cleanPageCount + 1 : 1;

                var production = DeepCloneDocument(source);
                if (ReferenceEquals(production, source) && documentFactory != null)
                {
                    var recreated = documentFactory();
                    if (recreated != null)
                        production = recreated;
                }

                if (!ReferenceEquals(clean, source) && !ReferenceEquals(production, source) && !ReferenceEquals(clean, production))
                    AppendInstance(clean, production);
            }

            return clean;
        }

        /// <summary>
        /// Дописывает второй экземпляр КП в конец предпросмотра. Первый блок
        /// получает BreakPageBefore: производственный комплект начинается с НОВОГО
        /// листа и никогда не «прилипает» к последней странице чистой копии.
        /// </summary>
        private static void AppendInstance(FlowDocument target, FlowDocument instance)
        {
            bool first = true;
            while (instance.Blocks.Count > 0)
            {
                var block = instance.Blocks.First();   // BlockCollection без индексатора
                instance.Blocks.Remove(block);
                if (first)
                {
                    block.BreakPageBefore = true;
                    first = false;
                }
                target.Blocks.Add(block);
            }
        }

        /// <summary>Число страниц документа (принудительная пагинация, как в печати).</summary>
        private static int CountPages(FlowDocument doc)
        {
            try
            {
                var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
                paginator.ComputePageCount();
                return Math.Max(0, paginator.PageCount);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintPreviewControl] CountPages failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Пересобирает предпросмотр в порядке «чистое → в производство» и
        /// синхронизирует overlay штампа/димминг/индикатор страниц.
        /// </summary>
        private void RebuildPreviewDocument(bool includeProductionCopy)
        {
            if (_document == null || DocumentReader == null) return;

            // Повторная сборка того же документа с тем же флажком не нужна:
            // RestoreSettings() в Initialize может поднять Checked-событие, которое
            // уже перестроило документ (см. alreadyBuilt) — повторный вызов это
            // отсекает.
            bool alreadyBuilt = ReferenceEquals(_previewBuiltFrom, _document)
                && _previewBuiltWithProduction == includeProductionCopy
                && ReferenceEquals(DocumentReader.Document, _previewDocument);

            if (!alreadyBuilt)
            {
                _previewDocument = BuildPreviewDocument(
                    _document,
                    includeProductionCopy,
                    out _cleanPageCount,
                    out _productionStartPage,
                    documentFactory: () => _printService.BuildFlowDocument(_items, _clientInfo, _totalAmount, _amountInWords));
                DocumentReader.Document = _previewDocument;
                _previewBuiltFrom = _document;
                _previewBuiltWithProduction = includeProductionCopy;

                try
                {
                    ((IDocumentPaginatorSource)_previewDocument).DocumentPaginator.ComputePageCount();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PrintPreviewControl] Preview repagination failed: {ex.Message}");
                }

                if (_innerViewer != null)
                {
                    try { _innerViewer.GoToPage(1); } catch { /* viewer ещё не отображался */ }
                }
            }

            // Обновление — после перепагинации viewer'а: PageCount/MasterPageNumber
            // при смене Document пересчитываются в следующем проходе layout'а.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateStampOverlay();
                UpdateDimmingOverlay();
                UpdatePageIndicator();
            }), DispatcherPriority.Loaded);
        }

        private static FlowDocument DeepCloneDocument(FlowDocument source)
        {
            try
            {
                var oldCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
                System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                try
                {
                    var xaml = System.Windows.Markup.XamlWriter.Save(source);
                    var clone = (FlowDocument)System.Windows.Markup.XamlReader.Parse(xaml);
                    clone.PageWidth = source.PageWidth;
                    clone.PageHeight = source.PageHeight;
                    clone.PagePadding = source.PagePadding;
                    clone.ColumnWidth = source.ColumnWidth;
                    return clone;
                }
                finally
                {
                    System.Threading.Thread.CurrentThread.CurrentCulture = oldCulture;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintPreviewControl] DeepCloneDocument failed, " +
                    $"falling back to original: {ex.Message}");
                return source;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Lifecycle
        // ═══════════════════════════════════════════════════════════════

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            PageFromControl.ValueChanged -= RangeField_ValueChanged;
            PageToControl.ValueChanged -= RangeField_ValueChanged;
            SinglePageControl.ValueChanged -= RangeField_ValueChanged;
            CopiesControl.ValueChanged -= CopiesControl_ValueChanged;

            DisposePrinterQueues();
            _printerQueues.Clear();

            if (_pageNumberDescriptor != null && _innerViewer != null)
            {
                _pageNumberDescriptor.RemoveValueChanged(_innerViewer, OnPageNumberChanged);
                _pageNumberDescriptor = null;
            }

            if (_pageCountDescriptor != null && _innerViewer != null)
            {
                _pageCountDescriptor.RemoveValueChanged(_innerViewer, OnPageNumberChanged);
                _pageCountDescriptor = null;
            }
        }

        private void CopiesControl_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<int> e)
        {
            // No-op: copies value is read from settings when needed.
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PopulatePrinterList();
            RestoreSettings();

            PageFromControl.ValueChanged += RangeField_ValueChanged;
            PageToControl.ValueChanged += RangeField_ValueChanged;
            SinglePageControl.ValueChanged += RangeField_ValueChanged;
            CopiesControl.ValueChanged += CopiesControl_ValueChanged;

            _innerViewer = DocumentReader as FlowDocumentPageViewer;
            if (_innerViewer != null)
            {
                _pageNumberDescriptor = DependencyPropertyDescriptor.FromProperty(
                    FlowDocumentPageViewer.MasterPageNumberProperty,
                    typeof(FlowDocumentPageViewer));
                _pageNumberDescriptor.AddValueChanged(_innerViewer, OnPageNumberChanged);

                _pageCountDescriptor = DependencyPropertyDescriptor.FromProperty(
                    FlowDocumentPageViewer.PageCountProperty,
                    typeof(FlowDocumentPageViewer));
                _pageCountDescriptor.AddValueChanged(_innerViewer, OnPageNumberChanged);
            }

            HideBuiltInToolbar();
            UpdateDimmingOverlay();
            UpdatePageIndicator();
        }

        private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded || PreviewScroller == null) return;
            if (PreviewScroller.ViewportWidth <= 0) return;

            const double pageW = 794.0;
            const double pageH = 1123.0;
            double scaleX = PreviewScroller.ViewportWidth / pageW;
            double scaleY = PreviewScroller.ViewportHeight / pageH;
            double needed = Math.Min(scaleX, scaleY);
            needed = Math.Min(needed, ZoomDefault);
            needed = Math.Max(needed, ZoomMin);

            if (_currentZoom > needed + 0.01)
            {
                _isUserZoom = false;
                _resizeDebounce.Stop();
                _resizeDebounce.Start();
            }
            else if (!_isUserZoom)
            {
                _resizeDebounce.Stop();
                _resizeDebounce.Start();
            }
        }

        private void DocumentReader_Loaded(object sender, RoutedEventArgs e)
        {
            if (DocumentReader.Document != null)
            {
                var paginator = ((IDocumentPaginatorSource)DocumentReader.Document).DocumentPaginator;
                paginator.ComputePageCount();
            }
            UpdateDimmingOverlay();
            UpdatePageIndicator();
            HideBuiltInToolbar();

            // Defer initial zoom past SizeChanged debounce (80ms) to avoid race:
            // SizeChanged → _resizeDebounce → FitToViewport() would overwrite
            // the fit+20% zoom set here. Dispatching at Loaded priority ensures
            // our zoom wins.
            // Stop the pending debounce too — the tick handler calls FitToViewport()
            // unconditionally, so even with _isUserZoom=true it would still overwrite.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _resizeDebounce.Stop();  // cancel pending auto-refit
                // 95% — комфортный зум для окна 1200×760
                _isUserZoom = true;
                ApplyZoom(0.95);
            }), DispatcherPriority.Loaded);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Printer list
        // ═══════════════════════════════════════════════════════════════

        private void PopulatePrinterList()
        {
            PrinterCombo.Items.Clear();
            DisposePrinterQueues();
            _printerQueues.Clear();
            _printerQueues.AddRange(PrintService.GetInstalledPrintQueues());

            if (_printerQueues.Count == 0)
            {
                PrinterCombo.DisplayMemberPath = null;
                PrinterCombo.Items.Add("Принтеры не найдены");
                PrinterCombo.SelectedIndex = 0;
                PrinterCombo.IsEnabled = false;
                PrintButton.IsEnabled = false;
                return;
            }

            PrinterCombo.IsEnabled = true;
            PrinterCombo.DisplayMemberPath = "FullName";
            foreach (var queue in _printerQueues)
                PrinterCombo.Items.Add(queue);

            string? target = _settings.PrinterName ?? PrintService.GetDefaultPrinterName();
            int targetIndex = -1;
            if (!string.IsNullOrWhiteSpace(target))
            {
                targetIndex = _printerQueues.FindIndex(queue =>
                    string.Equals(queue.FullName, target, StringComparison.OrdinalIgnoreCase));
            }

            // Never silently switch to the first device when a saved target
            // disappeared. Leave the selection empty and disable Print until
            // the user explicitly picks an available queue.
            PrinterCombo.SelectedIndex = targetIndex;
            if (targetIndex < 0)
            {
                PrinterCombo.SelectedItem = null;
                PrintButton.IsEnabled = false;
                PrinterCombo.ToolTip = "Сохранённый принтер недоступен. Выберите доступную очередь.";
            }
        }

        private void DisposePrinterQueues()
        {
            foreach (var queue in _printerQueues)
            {
                try { queue.Dispose(); }
                catch (Exception ex) { Debug.WriteLine($"[PrintPreviewControl] Printer queue dispose failed: {ex.Message}"); }
            }
        }

        private void PrinterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PrinterCombo.SelectedItem is PrintQueue)
            {
                PrintButton.IsEnabled = true;
                PrinterCombo.ToolTip = null;
            }
            else if (_printerQueues.Count > 0)
            {
                PrintButton.IsEnabled = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Settings
        // ═══════════════════════════════════════════════════════════════

        private void RestoreSettings()
        {
            CopiesControl.Value = Math.Max(0, _settings.Copies);
            ProductionCopyCheck.IsChecked = _settings.IncludeProductionCopy;
            CollatedCheck.IsChecked = _settings.Collated;
            ColorBtn.IsChecked = _settings.Color;
            BwBtn.IsChecked = !_settings.Color;

            switch (_settings.Pages)
            {
                case PageMode.Range:
                    PageModeRange.IsChecked = true;
                    break;
                case PageMode.Single:
                    PageModeSingle.IsChecked = true;
                    break;
                default:
                    PageModeAll.IsChecked = true;
                    break;
            }

            PageFromControl.Value = Math.Max(1, _settings.PageFrom);
            PageToControl.Value = Math.Max(1, Math.Max(PageFromControl.Value, _settings.PageTo));
            SinglePageControl.Value = Math.Max(1, _settings.SinglePage);

            UpdatePageModeVisibility();

            // Если флажок «В производство» уже был выключен (и не менялся),
            // WPF не поднимет Unchecked-событие при присваивании IsChecked=false,
            // поэтому UpdateCopiesMinimum() не вызовется сам — вызываем явно,
            // чтобы Minimum для «Копии» стал 1 (нельзя поставить 0 без печати).
            UpdateCopiesMinimum();
        }

        private void CollectSettingsInto(PrintSettings target)
        {
            if (PrinterCombo.SelectedItem is PrintQueue selectedQueue && PrinterCombo.IsEnabled)
                target.PrinterName = selectedQueue.FullName;
            target.Copies = Math.Max(0, CopiesControl.Value);
            target.IncludeProductionCopy = ProductionCopyCheck.IsChecked == true;
            // Нормализация: без копии «В производство» 0 копий недопустимы —
            // печатать было бы нечего. Не даём сохранить такое состояние.
            if (!target.IncludeProductionCopy)
                target.Copies = Math.Max(1, target.Copies);
            target.Collated = CollatedCheck.IsChecked == true;
            target.Color = ColorBtn.IsChecked == true;

            if (PageModeRange.IsChecked == true)
                target.Pages = PageMode.Range;
            else if (PageModeSingle.IsChecked == true)
                target.Pages = PageMode.Single;
            else
                target.Pages = PageMode.All;

            int from = PageFromControl.Value;
            int to = PageToControl.Value;
            target.PageFrom = Math.Max(1, from);
            target.PageTo = Math.Max(target.PageFrom, to);

            if (target.PageFrom > target.PageTo)
                (target.PageFrom, target.PageTo) = (target.PageTo, target.PageFrom);

            target.SinglePage = Math.Max(1, SinglePageControl.Value);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Доп. копия в производство — предпросмотр печати
        //  Печать накладывается ОТДЕЛЬНЫМ слоем поверх страницы (StampOverlay),
        //  поэтому НЕ сдвигает заголовок/таблицу вниз — попадает в уже
        //  существующую свободную зону в верхнем левом углу.
        // ═══════════════════════════════════════════════════════════════

        private void ProductionCopyCheck_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCopiesMinimum();
            // Пересборка предпросмотра: чистое КП идёт первым, копия
            // «В производство» — вторым блоком (как реально печатается).
            RebuildPreviewDocument(ProductionCopyCheck.IsChecked == true);
        }

        /// <summary>
        /// «Копии» можно уменьшить до 0 ТОЛЬКО когда включена копия «В производство»
        /// (0 = напечатать только лист со штампом). При выключенном флажке минимум = 1,
        /// чтобы нельзя было получить «пустой» заказ на печать.
        /// </summary>
        private void UpdateCopiesMinimum()
        {
            if (CopiesControl == null) return;

            bool allowZero = ProductionCopyCheck.IsChecked == true;
            CopiesControl.Minimum = allowZero ? 0 : 1;
            if (CopiesControl.Value < CopiesControl.Minimum)
                CopiesControl.Value = CopiesControl.Minimum;
        }

        private void UpdateStampOverlay()
        {
            if (StampOverlay == null || StampImage == null) return;

            if (ProductionCopyCheck.IsChecked == true
                && StampImage.Source == null
                && ProductionStampImage.TryGetPath(out var path))
            {
                StampImage.Source = new BitmapImage(new Uri(path, UriKind.Absolute));
            }

            ApplyStampScale();
            GateStampToFirstPage();
        }

        /// <summary>
        /// Печать ставится только на ПЕРВУЮ страницу производственной копии, поэтому
        /// в предпросмотре overlay виден только на странице _productionStartPage —
        /// первой странице производственного блока, который идёт ПОСЛЕ чистого КП.
        /// </summary>
        private void GateStampToFirstPage()
        {
            if (StampOverlay == null) return;

            bool includeStamp = ProductionCopyCheck.IsChecked == true;
            int currentPage = _innerViewer != null ? _innerViewer.MasterPageNumber : 1;
            StampOverlay.Visibility =
                (includeStamp && _productionStartPage > 0 && currentPage == _productionStartPage)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void ApplyStampScale()
        {
            if (StampImage == null) return;

            // Печать масштабируется вместе с документом (зум) и держится
            // у верхнего левого угла страницы без сдвига контента.
            double s = _currentZoom;
            StampImage.Width = ProductionStampImage.WidthDip * s;
            StampImage.Height = ProductionStampImage.HeightDip * s;
            // Те же отступы, что и в печати/PDF: левый — на левой линии контента КП,
            // верх — в свободной зоне над заголовком (единые константы ProductionStampImage).
            StampImage.Margin = new Thickness(
                ProductionStampImage.LeftOffsetDip * s,
                ProductionStampImage.TopOffsetDip * s,
                0, 0);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Page mode radio buttons
        // ═══════════════════════════════════════════════════════════════

        private void PageMode_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePageModeVisibility();
            UpdateDimmingOverlay();
        }

        private void UpdatePageModeVisibility()
        {
            if (RangeFieldsBorder != null)
                RangeFieldsBorder.Visibility = PageModeRange.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (SinglePageBorder != null)
                SinglePageBorder.Visibility = PageModeSingle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Range field debounce
        // ═══════════════════════════════════════════════════════════════

        private void RangeField_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<int> e)
        {
            _textChangedDebounce.Stop();
            _textChangedDebounce.Start();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Dimming overlay
        // ═══════════════════════════════════════════════════════════════

        private void OnPageNumberChanged(object? sender, EventArgs e)
        {
            UpdateDimmingOverlay();
            UpdatePageIndicator();
        }

        private void UpdateDimmingOverlay()
        {
            if (!IsInitialized) return;

            if (_innerViewer == null || _innerViewer.PageCount == 0)
            {
                DimmingOverlay.Visibility = Visibility.Collapsed;
                UpdateRangeHint(0, 0);
                return;
            }

            int totalPages = _innerViewer.PageCount;
            int currentPage = _innerViewer.MasterPageNumber;

            string rangeText;
            bool isOutOfRange;

            if (PageModeAll.IsChecked == true)
            {
                isOutOfRange = false;
                rangeText = "";
            }
            else if (PageModeSingle.IsChecked == true)
            {
                int sp = Math.Max(1, SinglePageControl.Value);
                isOutOfRange = currentPage != sp;
                rangeText = $"({sp})";
            }
            else
            {
                int from = PageFromControl.Value;
                int to = PageToControl.Value;
                if (from >= 1 && to >= 1)
                {
                    int f = Math.Min(from, to);
                    int t = Math.Max(from, to);
                    isOutOfRange = currentPage < f || currentPage > t;
                    rangeText = f == t ? $"({f})" : $"({f}-{t})";
                }
                else
                {
                    isOutOfRange = false;
                    rangeText = "";
                }
            }

            // Производственный блок печатается ЦЕЛИКОМ вторым (и в физической печати,
            // и в PDF), поэтому его страницы не гасятся даже при выборе диапазона
            // или одной страницы — иначе предпросмотр показывал бы ненапечатаемую
            // копию «В производство».
            if (isOutOfRange && _productionStartPage > 0 && currentPage >= _productionStartPage)
                isOutOfRange = false;

            if (isOutOfRange)
            {
                DimmingText.Text = $"Стр. {currentPage} не входит в диапазон печати {rangeText}";
                DimmingOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                DimmingOverlay.Visibility = Visibility.Collapsed;
            }

            // Диапазон выбирается по ЧИСТОМУ комплекту (семантика печати), а не по
            // общему числу страниц предпросмотра — в него уже входит блок
            // «В производство», который печатается независимо от диапазона.
            int sourcePages = _cleanPageCount > 0 ? _cleanPageCount : totalPages;
            int printPages = GetPageCountForRange(sourcePages);
            if (_productionStartPage > 0)
                printPages += sourcePages;   // производственный блок печатается целиком
            UpdateRangeHint(totalPages, printPages);
        }

        private void UpdateRangeHint(int totalPages, int rangePages)
        {
            if (totalPages == 0)
            {
                RangeHint.Text = "Документ не пагинирован";
                return;
            }

            if (PageModeAll.IsChecked == true)
                RangeHint.Text = $"Все страницы ({totalPages} стр.)";
            else
                RangeHint.Text = $"На печать: {rangePages} стр. из {totalPages}";
        }

        private int GetPageCountForRange(int totalPages)
            => PrintPageRangeCalculator.Count(
                GetSelectedPageMode(),
                totalPages,
                PageFromControl.Value,
                PageToControl.Value,
                SinglePageControl.Value);

        private PageMode GetSelectedPageMode()
        {
            if (PageModeRange.IsChecked == true)
                return PageMode.Range;
            if (PageModeSingle.IsChecked == true)
                return PageMode.Single;
            return PageMode.All;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Page navigation
        // ═══════════════════════════════════════════════════════════════

        // Навигация НЕ полагается на CanGoToNextPage/CanGoToPreviousPage (read-only DP
        // DocumentViewerBase): после замены Document (пересборка предпросмотра с копией
        // «В производство») они устаревают — индикатор показывает «1 / 2», а
        // CanGoToNextPage остаётся false, и клик молча игнорируется (баг «кнопки
        // переключения страниц не работают»). Переход — через GoToPage по арифметике
        // от PageCount/MasterPageNumber: CanGoToPage сверяется именно с PageCount — тем
        // числом, что показано в индикаторе.
        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            var viewer = _innerViewer ?? (DocumentReader as FlowDocumentPageViewer);
            if (viewer == null) return;

            int current = viewer.MasterPageNumber;
            if (current > 1)
            {
                viewer.GoToPage(current - 1);
                PreviewScroller?.ScrollToVerticalOffset(0);
            }
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            var viewer = _innerViewer ?? (DocumentReader as FlowDocumentPageViewer);
            if (viewer == null) return;

            int current = viewer.MasterPageNumber;
            int total = viewer.PageCount;
            if (total > 0 && current < total)
            {
                viewer.GoToPage(current + 1);
                PreviewScroller?.ScrollToVerticalOffset(0);
            }
        }

        private void UpdatePageIndicator()
        {
            if (PageIndicator == null || _innerViewer == null) return;

            int total = _innerViewer.PageCount;
            int current = _innerViewer.MasterPageNumber;

            PageIndicator.Text = total > 0 ? $"{current} / {total}" : "—";

            // Включение кнопок считается из ТЕХ ЖЕ чисел, что и индикатор
            // (PageCount/MasterPageNumber), а не из CanGoTo* — эти DP после замены
            // Document не пересчитываются и врут (см. комментарий в NextPage_Click).
            if (PrevPageBtn != null)
                PrevPageBtn.IsEnabled = current > 1;
            if (NextPageBtn != null)
                NextPageBtn.IsEnabled = total > 0 && current < total;

            GateStampToFirstPage();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Print
        // ═══════════════════════════════════════════════════════════════

        private async void Print_Click(object sender, RoutedEventArgs e)
        {
            var attempt = _settings.Clone();
            CollectSettingsInto(attempt);

            if (attempt.Copies > 10)
            {
                // UX-06: fluent confirm instead of raw MessageBox.
                if (!DialogService.ShowConfirm(
                        $"Количество копий ({attempt.Copies}) больше 10.\nВы точно хотите распечатать?",
                        "Печать",
                        Window.GetWindow(this)))
                    return;
            }

            if (attempt.Copies <= 0 && !attempt.IncludeProductionCopy)
            {
                DialogService.ShowMessage(
                    "Укажите хотя бы одну копию или включите копию «В производство».",
                    "Печать",
                    Window.GetWindow(this));
                return;
            }

            WarnIfProductionStampMissing(attempt);

            PrintQueue? queue = PrinterCombo.SelectedItem as PrintQueue;
            if (queue == null)
            {
                DialogService.ShowMessage(
                    "Не удалось определить выбранный принтер.\nОбновите окно предпросмотра и попробуйте снова.",
                    "Ошибка печати",
                    Window.GetWindow(this));
                return;
            }

            // Keep the persisted setting aligned with the exact queue object
            // selected in the UI, including UNC/server context.
            attempt.PrinterName = queue.FullName;

            PrintTicket ticket;
            try
            {
                ticket = queue.DefaultPrintTicket.Clone();
                ticket.OutputColor = attempt.Color
                    ? OutputColor.Color
                    : OutputColor.Monochrome;
                // Explicit A4 — prevents XPS writer from applying the printer's
                // default media size (e.g. Letter, 4×6 photo, etc.) as the page
                // size on the paginator chain. Without this, the XPS writer may
                // set PageWidth to a value incompatible with FlowDocument's
                // ColumnWidth=Infinity → paragraph width becomes NaN.
                ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
                // Явно задаём прямой порядок страниц (1, 2, ...), чтобы чистый экземпляр
                // всегда печатался первым, а экземпляр «В ПРОИЗВОДСТВО» — вторым,
                // даже если в настройках драйвера принтера по умолчанию включен Reverse
                // (например, у струйных Canon G-серии).
                ticket.PageOrder = PageOrder.Standard;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintPreviewControl] PrintTicket config failed: {ex.Message}");
                ticket = queue.DefaultPrintTicket;
            }

            // v3.43.2.6: switch to busy state BEFORE the heavy BuildFixedDocument
            // call so the user sees a progress bar + disabled Print button
            // during the multi-second 200-DPI rasterisation, instead of
            // perceiving it as a frozen UI. SetPrintingState(true) also resets
            // PrintProgressBar.Value=0; the bar advances again once SendToQueue
            // starts (SimulateProgressAsync ticks 1/copies).
            SetPrintingState(true);
            ProgressText.Text = "Подготовка документа к печати...";

            DocumentPaginator paginator;
            try
            {
                // ── v3.43.2+: BuildFixedDocument (rayal packets)
                // Wraps the FlowDocument into a FixedDocument pre-rasterised at
                // 300 DPI with vector TextBlock header/footer overlays. Pages
                // are pre-buffered into a dictionary keyed by source index so
                // copies share the same BitmapSource. This bypasses the entire
                // DocumentPaginator wrapper bug (paragraphWidth must be finite)
                // by never wrapping FlowDocumentPaginator in a subclass.
                var fixedDoc = PrintService.BuildFixedDocument(
                    _document, attempt, _contractNumber ?? "", DateTime.Now);
                paginator = fixedDoc.DocumentPaginator;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintPreviewControl] BuildFixedDocument failed: {ex}");
                // ex.ToString() includes stack trace so we can pinpoint which
                // constructor / callsite throws. Surface it in error dialog.
                string details = $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
                Clipboard.SetText(details);
                DialogService.ShowMessage(
                    $"Ошибка подготовки документа к печати:\n{ex.Message}\n\n" +
                    $"Тип: {ex.GetType().Name}\n" +
                    $"Полный stack trace скопирован в буфер обмена.",
                    "Ошибка печати",
                    Window.GetWindow(this));
                SetPrintingState(false);   // v3.43.2.6: re-enable button after failed build
                return;
            }

            string jobName = string.IsNullOrWhiteSpace(_contractNumber)
                ? "КП"
                : $"КП №{_contractNumber.Trim()}";

            SetPrintingState(true);   // resets ProgressText to "Отправка в очередь..." (good Phase-2 default)

            try
            {
                var printResult = PrintService.SendToQueue(queue, jobName, paginator, ticket);

                if (printResult.Type == PrintResultType.Success)
                {
                    _settings = attempt;
                    await SimulateProgressAsync(
                        Math.Max(1, attempt.Copies + (attempt.IncludeProductionCopy ? 1 : 0)));
                    // Re-enable UI before closing — in overlay mode
                    // Closed may have no subscribers, leaving button disabled.
                    SetPrintingState(false);
                    // Fire Closed event instead of closing window
                    Closed?.Invoke(this, EventArgs.Empty);
                }
                else if (printResult.IsRetryable)
                {
                    // UX-06: fluent confirm; the answer is now honoured — "Да"
                    // re-runs the print flow (the raw MessageBox previously
                    // asked the same question and discarded the answer).
                    SetPrintingState(false);
                    if (DialogService.ShowConfirm(
                            printResult.UserMessage + "\n\nПовторить отправку на печать?",
                            "Ошибка принтера",
                            Window.GetWindow(this)))
                    {
                        Print_Click(sender, e);
                    }
                }
                else
                {
                    DialogService.ShowMessage(
                        printResult.UserMessage,
                        "Ошибка печати",
                        Window.GetWindow(this));
                    SetPrintingState(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintPreviewControl] Print_Click unexpected: {ex}");
                DialogService.ShowMessage(
                    $"Неожиданная ошибка: {ex.Message}",
                    "Ошибка",
                    Window.GetWindow(this));
                SetPrintingState(false);
            }
        }

        private void SetPrintingState(bool isPrinting)
        {
            PrintButton.IsEnabled = !isPrinting;
            PrintProgressBar.Visibility = isPrinting ? Visibility.Visible : Visibility.Collapsed;
            ProgressText.Visibility = isPrinting ? Visibility.Visible : Visibility.Collapsed;
            if (isPrinting)
            {
                PrintProgressBar.Value = 0;
                ProgressText.Text = "Отправка в очередь...";
            }
        }

        private async Task SimulateProgressAsync(int copies)
        {
            int delayMs = Math.Max(20, Math.Min(200, 2000 / copies));
            for (int i = 1; i <= copies; i++)
            {
                PrintProgressBar.Value = (double)i / copies * 100;
                ProgressText.Text = $"Копия {i} из {copies}...";
                await Task.Delay(delayMs);
            }
            ProgressText.Text = "Готово!";
            await Task.Delay(300);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Drawing EdgeMode toggle (Проблема 2)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Проходит по всему FlowDocument и меняет EdgeMode на всех
        /// Image-чертежах. Aliased — для печати 600 DPI (резкие линии),
        /// Unspecified — для экранного превью (сглаживание).
        /// </summary>
        private void SetDrawingsEdgeMode(bool AliasedForPrint)
        {
            var target = AliasedForPrint ? EdgeMode.Aliased : EdgeMode.Unspecified;
            foreach (var block in _document?.Blocks ?? Enumerable.Empty<Block>())
                SetEdgeModeInBlock(block, target);
        }

        private static void SetEdgeModeInBlock(Block block, EdgeMode mode)
        {
            switch (block)
            {
                case Table table:
                    foreach (var rowGroup in table.RowGroups)
                    foreach (var row in rowGroup.Rows)
                    foreach (var cell in row.Cells)
                    foreach (var inner in cell.Blocks)
                        SetEdgeModeInBlock(inner, mode);
                    break;
                case Section sec:
                    foreach (var inner in sec.Blocks)
                        SetEdgeModeInBlock(inner, mode);
                    break;
                case BlockUIContainer container:
                    if (container.Child is Image img)
                        RenderOptions.SetEdgeMode(img, mode);
                    break;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Save PDF
        // ═══════════════════════════════════════════════════════════════

        private void SavePdf_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0)
            {
                DialogService.ShowMessage(
                    "Нет позиций для экспорта в PDF.",
                    "Экспорт PDF",
                    Window.GetWindow(this));
                return;
            }

            // Generate filename from address (same pattern as ExportSelectedOrder)
            string raw = (_clientInfo.ClientAddress ?? string.Empty).Replace('/', ' ');
            string address = SanitizeFileName(raw);
            if (!string.IsNullOrEmpty(address))
            {
                address = address.ToUpperInvariant();
                address = string.Join(" ", address.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
            string contractNum = string.IsNullOrWhiteSpace(_contractNumber) ? "бн" : _contractNumber.Trim();
            string defaultName = string.IsNullOrEmpty(address)
                ? $"КП {contractNum}.pdf"
                : $"{address} {contractNum}.pdf";

            var dlg = new SaveFileDialog
            {
                Title = "Сохранить КП как PDF",
                Filter = "PDF файлы (*.pdf)|*.pdf|Все файлы (*.*)|*.*",
                DefaultExt = ".pdf",
                FileName = defaultName
            };

            if (dlg.ShowDialog() != true) return;

            var currentSettings = CollectCurrentSettings();

            if (currentSettings.Copies <= 0 && !currentSettings.IncludeProductionCopy)
            {
                DialogService.ShowMessage(
                    "Укажите хотя бы одну копию или включите копию «В производство».",
                    "Экспорт PDF",
                    Window.GetWindow(this));
                return;
            }

            WarnIfProductionStampMissing(currentSettings);

            try
            {
                _printService.ExportPdf(dlg.FileName, _items, _clientInfo, _totalAmount, _amountInWords, attemptSettings: currentSettings);
                DialogService.ShowMessage(
                    $"КП сохранён в PDF:\n{dlg.FileName}",
                    "Экспорт PDF",
                    Window.GetWindow(this));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SavePdf_Click] ExportPdf failed: {ex}");
                Clipboard.SetText(ex.ToString());
                DialogService.ShowMessage(
                    $"Ошибка при сохранении PDF:\n{ex.Message}\n\n" +
                    $"Тип: {ex.GetType().Name}\n" +
                    $"Полный stack trace и QuestPDF layout tree скопированы в буфер обмена.",
                    "Экспорт PDF",
                    Window.GetWindow(this));
            }
        }

        /// <summary>
        /// Удаляет символы, недопустимые в именах файлов Windows.
        /// </summary>
        private PrintSettings CollectCurrentSettings()
        {
            var current = _settings.Clone();
            CollectSettingsInto(current);
            return current;
        }

        /// <summary>
        /// Если включена производственная копия, а изображение печати недоступно —
        /// предупреждаем пользователя один раз (политика: продолжить, но без печати).
        /// </summary>
        private void WarnIfProductionStampMissing(PrintSettings settings)
        {
            if (settings.IncludeProductionCopy && !ProductionStampImage.TryGetPath(out _))
            {
                DialogService.ShowMessage(
                    "Изображение печати «В ПРОИЗВОДСТВО» не найдено.\n" +
                    "Копия в производство будет сформирована БЕЗ печати.\n\n" +
                    "Проверьте, что файл ВПРОИЗВОДСТВО.png находится рядом с программой.",
                    "Печать «В производство»",
                    Window.GetWindow(this));
            }
        }

        internal static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var chars = name.Select(c => invalid.Contains(c) ? ' ' : c).ToArray();
            return new string(chars).Trim();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Zoom
        // ═══════════════════════════════════════════════════════════════

        private void FitToViewport()
        {
            try
            {
                if (PreviewScroller.ViewportWidth <= 0 || PreviewScroller.ViewportHeight <= 0)
                    return;

                const double pageW = 794.0;
                const double pageH = 1123.0;

                double scaleX = PreviewScroller.ViewportWidth / pageW;
                double scaleY = PreviewScroller.ViewportHeight / pageH;
                double fitScale = Math.Min(scaleX, scaleY);

                fitScale = Math.Min(fitScale, ZoomDefault);
                fitScale = Math.Max(fitScale, ZoomMin);

                _isUserZoom = false;
                _fitZoom = fitScale;
                ApplyZoom(fitScale);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintPreviewControl] FitToViewport failed: {ex.Message}");
            }
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e) { _isUserZoom = true; ApplyZoom(_currentZoom - ZoomStep); }
        private void ZoomIn_Click(object sender, RoutedEventArgs e) { _isUserZoom = true; ApplyZoom(_currentZoom + ZoomStep); }
        private void ZoomReset_Click(object sender, RoutedEventArgs e) { _isUserZoom = false; ApplyZoom(ZoomDefault); }
        private void ZoomFit_Click(object sender, RoutedEventArgs e) { _isUserZoom = false; FitToViewport(); }

        private DateTime _lastPageTurnTime = DateTime.MinValue;
        private const double PageTurnCooldownMs = 180;

        private void PreviewScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                e.Handled = true;
                _isUserZoom = true;
                double delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
                ApplyZoom(_currentZoom + delta);
                return;
            }

            if (PreviewScroller == null) return;

            // Shift + Wheel = горизонтальная прокрутка
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                if (PreviewScroller.ScrollableWidth > 0)
                {
                    double step = (e.Delta / 120.0) * 60.0;
                    PreviewScroller.ScrollToHorizontalOffset(
                        Math.Clamp(PreviewScroller.HorizontalOffset - step, 0, PreviewScroller.ScrollableWidth));
                }
                return;
            }

            // Обычная прокрутка колесом мыши:
            // Перехватываем событие ДО DocumentReader (FlowDocumentPageViewer),
            // так как встроенный обработчик FlowDocumentPageViewer при первом же щелчке
            // колеса насильно скипает страницу через NextPage/PrevPage, не давая прокрутить
            // лист по вертикали и оставляя камеру в самом конце листа.
            e.Handled = true;

            int rawDelta = e.Delta;
            if (rawDelta == 0) return;

            var viewer = _innerViewer ?? (DocumentReader as FlowDocumentPageViewer);
            int currentPage = viewer != null ? viewer.MasterPageNumber : 1;
            int totalPages = viewer != null ? viewer.PageCount : 1;

            double currentOffset = PreviewScroller.VerticalOffset;
            double maxOffset = PreviewScroller.ScrollableHeight;

            // Величина прокрутки за один щелчок колеса: адаптивно по настройкам Windows, минимум 48 DIP
            double lines = SystemParameters.WheelScrollLines > 0 ? SystemParameters.WheelScrollLines : 3;
            double stepPerNotch = Math.Max(48.0, lines * 18.0);
            double scrollAmount = (Math.Abs(rawDelta) / 120.0) * stepPerNotch;

            if (rawDelta < 0)
            {
                // Прокрутка ВНИЗ
                if (maxOffset > 1 && currentOffset < maxOffset - 1)
                {
                    // Внутри текущего листа есть куда прокручивать вниз
                    double newOffset = Math.Min(maxOffset, currentOffset + scrollAmount);
                    PreviewScroller.ScrollToVerticalOffset(newOffset);
                }
                else
                {
                    // Достигли самого низа листа (или лист целиком помещается на экране).
                    // Следующий жест колеса вниз перелистывает на СЛЕДУЮЩУЮ страницу.
                    if (viewer != null && currentPage < totalPages)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - _lastPageTurnTime).TotalMilliseconds >= PageTurnCooldownMs)
                        {
                            _lastPageTurnTime = now;
                            viewer.GoToPage(currentPage + 1);
                            // Камера ОБЯЗАНА встать в НАЧАЛО (вверх) нового листа, а не в конец!
                            PreviewScroller.ScrollToVerticalOffset(0);
                        }
                    }
                }
            }
            else
            {
                // Прокрутка ВВЕРХ
                if (currentOffset > 1)
                {
                    // Внутри текущего листа есть куда прокручивать вверх
                    double newOffset = Math.Max(0, currentOffset - scrollAmount);
                    PreviewScroller.ScrollToVerticalOffset(newOffset);
                }
                else
                {
                    // Достигли самого верха листа (или лист целиком помещается на экране).
                    // Следующий жест колеса вверх возвращает на ПРЕДЫДУЩУЮ страницу.
                    if (viewer != null && currentPage > 1)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - _lastPageTurnTime).TotalMilliseconds >= PageTurnCooldownMs)
                        {
                            _lastPageTurnTime = now;
                            viewer.GoToPage(currentPage - 1);
                            // При возврате вверх: если предыдущий лист больше экрана,
                            // ставим камеру в КОНЕЦ (низ) предыдущего листа, чтобы
                            // пользователь мог непрерывно прокручивать его снизу вверх.
                            // Если помещается целиком — наверх (0).
                            if (maxOffset > 1)
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    PreviewScroller?.ScrollToBottom();
                                }), DispatcherPriority.Loaded);
                            }
                            else
                            {
                                PreviewScroller.ScrollToVerticalOffset(0);
                            }
                        }
                    }
                }
            }
        }

        private void ApplyZoom(double scale)
        {
            try
            {
                _currentZoom = Math.Clamp(scale, ZoomMin, ZoomMax);
                DocumentReader.Zoom = _currentZoom * 100;
                UpdateZoomLabel();
                CenterScrollViewer();
                ApplyStampScale();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintPreviewControl] ApplyZoom failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Центрирует ScrollViewer ПО ГОРИЗОНТАЛИ после изменения зума.
        /// v3.43.3: убрана вертикальная центровка — она «телепортировала» пользователя
        /// в середину страницы при каждом zoom in/out, ломая привычный рабочий процесс
        /// (юзер скроллировал вниз → приблизил → страница прыгнула в центр).
        /// Теперь: горизонталь стабильно по центру, вертикаль остаётся там, где пользователь её оставил.
        /// </summary>
        private void CenterScrollViewer()
        {
            if (PreviewScroller == null || PreviewContainer == null) return;
            // Defer to after layout pass — ActualWidth/ActualHeight ещё не обновлены.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (PreviewScroller.ExtentWidth > PreviewScroller.ViewportWidth)
                    PreviewScroller.ScrollToHorizontalOffset(
                        (PreviewScroller.ExtentWidth - PreviewScroller.ViewportWidth) / 2);
                // Intentionally NOT calling ScrollToVerticalOffset: vertical scroll position
                // is preserved between zoom operations so the user stays on the section of
                // the document they were inspecting (e.g. mid-page footer or signature line).
            }), DispatcherPriority.Render);
        }

        private void UpdateZoomLabel()
        {
            if (ZoomLabel == null) return;
            ZoomLabel.Text = $"{_currentZoom * 100:F0} %";
            if (ZoomOutBtn != null)
                ZoomOutBtn.IsEnabled = _currentZoom > ZoomMin + 0.05;
            if (ZoomInBtn != null)
                ZoomInBtn.IsEnabled = _currentZoom < ZoomMax - 0.05;
            if (ZoomResetBtn != null)
                ZoomResetBtn.IsEnabled = Math.Abs(_currentZoom - ZoomDefault) > 0.05;
            // UX-08: Fit+20% button removed — no per-button fit label needed.
            if (ZoomFitBtn != null)
            {
                ZoomFitBtn.Content = $"{_fitZoom * 100:F0}%";
                ZoomFitBtn.IsEnabled = Math.Abs(_currentZoom - _fitZoom) > 0.05;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Toolbar hiding
        // ═══════════════════════════════════════════════════════════════

        private void HideBuiltInToolbar()
        {
            try
            {
                var tray = FindVisualChild<ToolBarTray>(DocumentReader);
                if (tray != null)
                    tray.Visibility = Visibility.Collapsed;

                var toolbar = FindVisualChild<ToolBar>(DocumentReader);
                if (toolbar != null)
                    toolbar.Visibility = Visibility.Collapsed;

                // В стандартном шаблоне WPF встроенная нижняя панель навигации и зума
                // лежит внутри DockPanel рядом с Border (в котором хостится DocumentPageView).
                // Скрываем все элементы DockPanel, кроме контейнера страницы.
                var dockPanel = FindVisualChild<DockPanel>(DocumentReader);
                if (dockPanel != null)
                {
                    foreach (UIElement child in dockPanel.Children)
                    {
                        if (child is not Border b || FindVisualChild<DocumentPageView>(b) == null)
                        {
                            child.Visibility = Visibility.Collapsed;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintPreviewControl] HideBuiltInToolbar failed: {ex.Message}");
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var deep = FindVisualChild<T>(child);
                if (deep != null) return deep;
            }
            return null;
        }
    }
}
