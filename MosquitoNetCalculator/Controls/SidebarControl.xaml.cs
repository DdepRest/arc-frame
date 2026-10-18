using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class SidebarControl : UserControl
    {
        public Border CardClientBorder => CardClient;
        public Border CardContractBorder => CardContract;
        public Border CardNotesBorder => CardNotes;
        public TextBox TxtPrefix => TxtContractPrefix;
        public TextBox TxtNumber => TxtContractNumber;
        public ComboBox CmbOrderStatus => CmbStatus;

        public SidebarControl()
        {
            InitializeComponent();
            // v3.50: initial dot color + live sync on user changes. Programmatic
            // SelectedItem assignment (MainWindow ctor) also fires SelectionChanged,
            // so the dot follows both paths.
            CmbStatus.SelectionChanged += (_, _) => UpdateStatusDot();

            // v3.53: точка статуса следует теме. Кисть берётся из тех же токенов,
            // что и бейдж в «Заказах», а ThemeService анимирует существующую кисть
            // на месте (fast path) — то есть точка докрашивается вместе с
            // интерфейсом. Подписка нужна для slow path, где ресурс заменяется
            // новым экземпляром. Отписываемся на Unloaded, иначе статическое
            // событие держало бы контрол живым.
            // v3.55: редактор примечаний читает и пишет ClientInfo.Notes.
            // Вставка И перетаскивание — только «как текст» (см. NotesPastingFilter):
            // Framework поднимает то же событие DataObject.Pasting и на drop,
            // поэтому отдельного обработчика дропа нет и быть не должно — он
            // заменил бы родной перенос выделения внутри редактора вставкой копии.
            DataContextChanged += (_, _) => HookNotesSource();
            DataObject.AddPastingHandler(RtfNotes, NotesPastingFilter);
            // Курсор не должен обещать drop, который ничего не вставит (картинка,
            // файл): гасим такое перетаскивание ещё на DragOver.
            RtfNotes.AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(RtfNotes_PreviewDragOver));

            Loaded += (_, _) =>
            {
                ThemeService.ThemeChanged -= UpdateStatusDot;
                ThemeService.ThemeChanged += UpdateStatusDot;
                UpdateStatusDot();
                HookNotesSource();
            };
            Unloaded += (_, _) =>
            {
                ThemeService.ThemeChanged -= UpdateStatusDot;
                UnhookNotesSource();
            };
        }

        /// <summary>
        /// v3.50: status pill dot — цвет того же статуса, что и у бейджа в
        /// «Заказах» (<see cref="Models.OrderStatuses.GetBadgeKeys"/>), из
        /// ресурсов темы. До v3.53 здесь была ВТОРАЯ палитра hex-литералами
        /// светлой темы: точка не следовала тёмной теме и отличалась от бейджа
        /// того же статуса на одном экране.
        /// </summary>
        private void UpdateStatusDot()
        {
            if (StatusDot == null || CmbStatus == null) return;
            string status = CmbStatus.SelectedItem?.ToString() ?? string.Empty;
            var (_, fgKey) = Models.OrderStatuses.GetBadgeKeys(status);

            if (Application.Current?.Resources[fgKey] is Brush brush)
                StatusDot.Fill = brush;
        }

        /// <summary>XAML SelectionChanged handler — refreshes the dot (no business logic).</summary>
        private void CmbStatus_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStatusDot();

        /// <summary>
        /// Resolves the parent MainWindow from DataContext, logging a diagnostic if the
        /// DataContext is not a MainWindow. Returns false in that case so callers can
        /// bail out gracefully.
        /// </summary>
        private bool TryGetMainWindow(string handlerName, [NotNullWhen(true)] out MainWindow? mw)
        {
            if (DataContext is MainWindow window)
            {
                mw = window;
                return true;
            }
            System.Diagnostics.Trace.WriteLine($"[SidebarControl] DataContext is not MainWindow ({DataContext?.GetType().Name ?? "null"}), handler '{handlerName}' skipped.");
            mw = null;
            return false;
        }

        private void TxtContractPrefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(TxtContractPrefix_TextChanged), out var mw)) return;
            if (mw._suppressContractNumberUpdate) return;
            if (!mw.IsInitialized) return;
            mw.SuppressPrefixSave = false;
            mw.UpdateContractNumber();
        }

        private void TxtContractPrefix_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(TxtContractPrefix_LostFocus), out var mw)) return;
            if (mw.SuppressPrefixSave) return;
            AppSettingsService.SaveContractPrefix(TxtContractPrefix.Text);
        }

        private void SelectAll_OnFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(() => tb.SelectAll());
        }

        // ─── Notes formatting toolbar ─────────────────────────────

        private void BtnBold_Click(object sender, RoutedEventArgs e) => RunNotesCommand(EditingCommands.ToggleBold);
        private void BtnItalic_Click(object sender, RoutedEventArgs e) => RunNotesCommand(EditingCommands.ToggleItalic);

        private void BtnColor_Click(object sender, RoutedEventArgs e)
        {
            ColorPopup.IsOpen = !ColorPopup.IsOpen;
        }

        private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Background is SolidColorBrush brush)
            {
                ApplyNotesColor(brush.Color);
                ColorPopup.IsOpen = false;
            }
        }

        private void ColorSwatch_CustomClick(object sender, MouseButtonEventArgs e)
        {
            var lastHex = AppSettingsService.LoadLastColor();
            var defaultColor = System.Drawing.Color.FromArgb(0x00, 0x5F, 0xB8);
            try
            {
                var parsed = System.Drawing.ColorTranslator.FromHtml(lastHex);
                if (!parsed.IsEmpty) defaultColor = parsed;
            }
            catch { /* invalid stored hex — use default */ }

            var dlg = new System.Windows.Forms.ColorDialog
            {
                Color = defaultColor,
                FullOpen = true
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                AppSettingsService.SaveLastColor($"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
                ApplyNotesColor(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
            }
            ColorPopup.IsOpen = false;
        }

        /// <summary>
        /// «Очистить форматирование»: как и прежняя regex-очистка разметки, снимает
        /// всё сразу — и свойства текста, и структуру списка. Поэтому собираем чистый
        /// текст документа и строим его заново: TextRange.Text маркеров списка не
        /// содержит, они часть оформления, а не текста.
        /// </summary>
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (RtfNotes?.Document == null) return;

            string plain = new TextRange(RtfNotes.Document.ContentStart, RtfNotes.Document.ContentEnd)
                .Text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .TrimEnd('\n');

            LoadNotesIntoEditor(plain);
            SyncNotesToModel();
            RtfNotes.Focus();
        }

        private void BtnList_Click(object sender, RoutedEventArgs e) => RunNotesCommand(EditingCommands.ToggleBullets);

        // ─── Notes: редактор с форматированием (v3.55) ──────────────────────────
        //
        // Поле примечаний — RichTextBox: «Ж» применяет формат к выделению, а не
        // вставляет маркеры, поэтому «код» разметки пользователь не видит вообще.
        // Хранимый формат при этом НЕ изменился: в заказе, печати КП и PDF живёт
        // та же строка, что разбирает NotesFormatter, а редактор лишь конвертирует
        // её в FlowDocument и обратно (NotesDocumentBuilder / NotesMarkupWriter).
        // Старые заказы с маркерами открываются как раньше.
        //
        // Модель обновляется в четырёх точках: набор текста (TextChanged), команды
        // форматирования (они НЕ поднимают TextChanged), уход фокуса (страховка на
        // любой другой путь правки) и смена источника (заказ/сброс/AI).

        private bool _syncingNotes;
        private ClientInfo? _notesSource;

        /// <summary>
        /// Выполняет команду форматирования над редактором и сразу отдаёт результат
        /// в модель: смена форматирования не поднимает TextChanged, поэтому без
        /// явной синхронизации заказ сохранился бы без неё.
        /// </summary>
        private void RunNotesCommand(RoutedUICommand command)
        {
            if (RtfNotes == null) return;

            RtfNotes.Focus();
            if (command.CanExecute(null, RtfNotes))
                command.Execute(null, RtfNotes);

            SyncNotesToModel();
        }

        /// <summary>
        /// Красит выделение. Если выделения нет, TextSelection ставит цвет и на
        /// каретку — текст, набранный сразу после выбора цвета, тоже будет цветным.
        /// </summary>
        private void ApplyNotesColor(Color color)
        {
            if (RtfNotes == null) return;

            var brush = new SolidColorBrush(color);
            brush.Freeze();

            RtfNotes.Focus();
            RtfNotes.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            SyncNotesToModel();
        }

        private void RtfNotes_TextChanged(object sender, TextChangedEventArgs e) => SyncNotesToModel();

        /// <summary>
        /// Страховка: форматирование можно сменить и помимо кнопок (контекстное
        /// меню), а уход фокуса — последняя точка, где правку ещё можно отдать в
        /// модель до сохранения заказа.
        /// </summary>
        private void RtfNotes_LostFocus(object sender, RoutedEventArgs e) => SyncNotesToModel();

        /// <summary>
        /// Ctrl+B / Ctrl+I идут через те же методы, что и кнопки: встроенные команды
        /// поменяли бы вид текста, не обновив модель. Ctrl+U глушим осознанно —
        /// хранимый формат подчёркивания не знает, и после перезагрузки оно бы молча
        /// пропало.
        /// </summary>
        private void RtfNotes_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;

            if (e.Key == Key.B)
            {
                RunNotesCommand(EditingCommands.ToggleBold);
                e.Handled = true;
            }
            else if (e.Key == Key.I)
            {
                RunNotesCommand(EditingCommands.ToggleItalic);
                e.Handled = true;
            }
            else if (e.Key == Key.U)
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// Барьер «только текст» у редактора примечаний — один на оба входа:
        /// вставку из буфера и перетаскивание. Framework поднимает то же событие
        /// <c>DataObject.Pasting</c> и на drop (<c>DataObjectPastingEventArgs.IsDragDrop</c>),
        /// поэтому дроп отдельного обработчика не требует: после подмены DataObject
        /// редактор применяет только <see cref="DataFormats.UnicodeText"/> —
        /// присвоение DataObject сбрасывает FormatToApply на формат нового объекта.
        ///
        /// Зачем: чужой шрифт, размер или подчёркивание из Word попали бы в
        /// документ, а при сохранении молча исчезли (формат знает ровно жирный,
        /// курсив, цвет и список). Картинка или файл из проводника — тем более:
        /// хранить их негде, поэтому <c>e.CancelCommand()</c>.
        ///
        /// Статический — чтобы тесты звали тот же самый барьер, а не его копию
        /// (страж <c>SidebarNotesDropTests</c>).
        /// </summary>
        internal static void NotesPastingFilter(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                e.CancelCommand();
                return;
            }

            var text = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
            e.DataObject = new DataObject(DataFormats.UnicodeText, text);
        }

        /// <summary>
        /// Перетаскивание без текста (картинка, файл из проводника) гасим ещё до
        /// drop: иначе курсор обещает вставку, которой заведомо не будет — фильтр
        /// такой payload отменит. Текстовые drag'и, включая перенос выделения
        /// внутри редактора, не трогаем: у них своё родное поведение (каретка
        /// drop, Move/Copy) и формат, который редактор умеет хранить.
        /// </summary>
        private void RtfNotes_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data == null || e.Data.GetDataPresent(DataFormats.UnicodeText)) return;

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// Подписывается на ClientInfo текущего заказа: загрузка, сброс и AI-правка
        /// примечаний приходят как PropertyChanged и перерисовывают редактор.
        /// </summary>
        private void HookNotesSource()
        {
            var info = (DataContext as MainWindow)?.ClientInfo;
            if (ReferenceEquals(info, _notesSource)) return;

            if (_notesSource != null)
                _notesSource.PropertyChanged -= OnNotesSourcePropertyChanged;

            _notesSource = info;

            if (_notesSource != null)
                _notesSource.PropertyChanged += OnNotesSourcePropertyChanged;

            LoadNotesIntoEditor(_notesSource?.Notes);
        }

        private void UnhookNotesSource()
        {
            if (_notesSource == null) return;

            _notesSource.PropertyChanged -= OnNotesSourcePropertyChanged;
            _notesSource = null;
        }

        private void OnNotesSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ClientInfo.Notes))
                LoadNotesIntoEditor(_notesSource?.Notes);
        }

        /// <summary>
        /// Показывает хранимую разметку в редакторе. Программная перестройка
        /// документа поднимает TextChanged — флаг не даёт ей тут же вернуться в
        /// модель (иначе получился бы цикл «модель → документ → модель»).
        /// </summary>
        private void LoadNotesIntoEditor(string? markup)
        {
            if (RtfNotes == null || _syncingNotes) return;

            _syncingNotes = true;
            try
            {
                RtfNotes.Document = NotesDocumentBuilder.Build(markup);
            }
            finally
            {
                _syncingNotes = false;
            }
        }

        /// <summary>
        /// Отдаёт содержимое редактора в модель в хранимом формате разметки —
        /// именно её читают сохранение заказа, печать КП и PDF.
        /// </summary>
        private void SyncNotesToModel()
        {
            if (RtfNotes?.Document == null || _notesSource == null || _syncingNotes) return;

            _syncingNotes = true;
            try
            {
                _notesSource.Notes = NotesMarkupWriter.ToMarkup(RtfNotes.Document, RtfNotes.Foreground);
            }
            finally
            {
                _syncingNotes = false;
            }
        }
    }
}
