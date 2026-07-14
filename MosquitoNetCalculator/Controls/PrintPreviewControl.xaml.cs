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
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
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
        private PrintService _printService;
        private string _contractNumber;
        private PrintSettings _settings;

        // ── PDF export data ──
        private List<OrderItem> _items = new();
        private ClientInfo _clientInfo = new();
        private double _totalAmount;
        private string _amountInWords = string.Empty;

        private FlowDocumentPageViewer? _innerViewer;
        private DependencyPropertyDescriptor? _pageNumberDescriptor;
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

            _previewDocument = DeepCloneDocument(document);
            DocumentReader.Document = _previewDocument;

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

            if (_pageNumberDescriptor != null && _innerViewer != null)
            {
                _pageNumberDescriptor.RemoveValueChanged(_innerViewer, OnPageNumberChanged);
                _pageNumberDescriptor = null;
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
            var printers = PrintService.GetInstalledPrinterNames();

            if (printers.Count == 0)
            {
                PrinterCombo.Items.Add("Принтеры не найдены");
                PrinterCombo.SelectedIndex = 0;
                PrinterCombo.IsEnabled = false;
                PrintButton.IsEnabled = false;
                return;
            }

            foreach (var name in printers)
                PrinterCombo.Items.Add(name);

            string? target = _settings.PrinterName ?? PrintService.GetDefaultPrinterName();
            if (!string.IsNullOrEmpty(target) && PrinterCombo.Items.Contains(target))
                PrinterCombo.SelectedItem = target;
            else if (PrinterCombo.Items.Count > 0)
                PrinterCombo.SelectedIndex = 0;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Settings
        // ═══════════════════════════════════════════════════════════════

        private void RestoreSettings()
        {
            CopiesControl.Value = Math.Max(1, _settings.Copies);
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
        }

        private void CollectSettingsInto(PrintSettings target)
        {
            if (PrinterCombo.SelectedItem is string printerName && PrinterCombo.IsEnabled)
                target.PrinterName = printerName;
            target.Copies = Math.Max(1, CopiesControl.Value);
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

            if (isOutOfRange)
            {
                DimmingText.Text = $"Стр. {currentPage} не входит в диапазон печати {rangeText}";
                DimmingOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                DimmingOverlay.Visibility = Visibility.Collapsed;
            }

            UpdateRangeHint(totalPages, GetPageCountForRange(totalPages));
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
        {
            if (PageModeAll.IsChecked == true) return totalPages;
            if (PageModeSingle.IsChecked == true)
            {
                int sp = SinglePageControl.Value;
                return (sp >= 1 && sp <= totalPages) ? 1 : 0;
            }
            int from = PageFromControl.Value;
            int to = PageToControl.Value;
            int f = Math.Max(1, Math.Min(from, to));
            int t = Math.Min(totalPages, Math.Max(from, to));
            return Math.Max(0, t - f + 1);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Page navigation
        // ═══════════════════════════════════════════════════════════════

        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_innerViewer != null && _innerViewer.CanGoToPreviousPage)
                _innerViewer.PreviousPage();
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_innerViewer != null && _innerViewer.CanGoToNextPage)
                _innerViewer.NextPage();
        }

        private void UpdatePageIndicator()
        {
            if (PageIndicator == null || _innerViewer == null) return;

            int total = _innerViewer.PageCount;
            int current = _innerViewer.MasterPageNumber;

            PageIndicator.Text = total > 0 ? $"{current} / {total}" : "—";

            if (PrevPageBtn != null)
                PrevPageBtn.IsEnabled = _innerViewer.CanGoToPreviousPage;
            if (NextPageBtn != null)
                NextPageBtn.IsEnabled = _innerViewer.CanGoToNextPage;
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
                var result = MessageBox.Show(
                    $"Количество копий ({attempt.Copies}) больше 10.\nВы точно хотите распечатать?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            string? printerName = attempt.PrinterName;
            PrintQueue? queue = PrintService.ResolvePrintQueue(printerName);
            if (queue == null)
            {
                MessageBox.Show(
                    "Не удалось найти ни одного принтера.\nУстановите принтер и попробуйте снова.",
                    "Ошибка печати",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

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
                MessageBox.Show(
                    $"Ошибка подготовки документа к печати:\n{ex.Message}\n\n" +
                    $"Тип: {ex.GetType().Name}\n" +
                    $"Полный stack trace скопирован в буфер обмена.",
                    "Ошибка печати",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
                    await SimulateProgressAsync(attempt.Copies);
                    // Re-enable UI before closing — in overlay mode
                    // Closed may have no subscribers, leaving button disabled.
                    SetPrintingState(false);
                    // Fire Closed event instead of closing window
                    Closed?.Invoke(this, EventArgs.Empty);
                }
                else if (printResult.IsRetryable)
                {
                    MessageBox.Show(
                        printResult.UserMessage + "\n\nПовторить отправку на печать?",
                        "Ошибка принтера",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    SetPrintingState(false);
                }
                else
                {
                    MessageBox.Show(
                        printResult.UserMessage,
                        "Ошибка печати",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    SetPrintingState(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintPreviewControl] Print_Click unexpected: {ex}");
                MessageBox.Show(
                    $"Неожиданная ошибка: {ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
                MessageBox.Show(
                    "Нет позиций для экспорта в PDF.",
                    "Экспорт PDF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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

            try
            {
                _printService.ExportPdf(dlg.FileName, _items, _clientInfo, _totalAmount, _amountInWords);
                MessageBox.Show(
                    $"КП сохранён в PDF:\n{dlg.FileName}",
                    "Экспорт PDF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SavePdf_Click] ExportPdf failed: {ex}");
                Clipboard.SetText(ex.ToString());
                MessageBox.Show(
                    $"Ошибка при сохранении PDF:\n{ex.Message}\n\n" +
                    $"Тип: {ex.GetType().Name}\n" +
                    $"Полный stack trace и QuestPDF layout tree скопированы в буфер обмена.",
                    "Экспорт PDF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Удаляет символы, недопустимые в именах файлов Windows.
        /// </summary>
        private static string SanitizeFileName(string name)
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
        private void ZoomFitPlus_Click(object sender, RoutedEventArgs e)
        {
            _isUserZoom = true;
            double fitPlus = Math.Min(ZoomDefault, _fitZoom + 0.2);
            ApplyZoom(fitPlus);
        }
        private void ZoomFit_Click(object sender, RoutedEventArgs e) { _isUserZoom = false; FitToViewport(); }

        private void PreviewScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                e.Handled = true;
                _isUserZoom = true;
                double delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
                ApplyZoom(_currentZoom + delta);
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
            double fitPlusZoom = Math.Min(ZoomDefault, _fitZoom + 0.2);
            if (ZoomFitPlusBtn != null)
            {
                ZoomFitPlusBtn.Content = $"{fitPlusZoom * 100:F0}%";
                ZoomFitPlusBtn.IsEnabled = Math.Abs(_currentZoom - fitPlusZoom) > 0.05;
            }
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
                {
                    tray.Visibility = Visibility.Collapsed;
                    return;
                }
                var toolbar = FindVisualChild<ToolBar>(DocumentReader);
                if (toolbar != null)
                    toolbar.Visibility = Visibility.Collapsed;
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
