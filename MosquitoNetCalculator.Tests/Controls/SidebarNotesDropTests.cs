using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Страж «drop в примечания — только как текст» (v3.55).
    ///
    /// Жалоба-аналог вставки: перетащенный из Word/браузера текст приходил с
    /// чужим жирным/цветом и попадал в документ, хотя формат хранения знает
    /// ровно жирный, курсив, цвет и список — при сохранении всё лишнее молча
    /// исчезало. Framework поднимает то же событие <c>DataObject.Pasting</c>
    /// и на drop, поэтому барьер у редактора один на оба входа.
    ///
    /// Проверяем механизм на живом <see cref="RichTextBox"/> (без SidebarControl,
    /// чей конструктор тянет MainWindow) и сам фильтр напрямую.
    /// </summary>
    public class SidebarNotesDropTests
    {
        private readonly ITestOutputHelper _out;

        public SidebarNotesDropTests(ITestOutputHelper output) => _out = output;

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator", "App.xaml")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Не найден корень репозитория.");
        }

        /// <summary>
        /// Проверяет production-шов дропа: framework на drop вызывает
        /// <c>TextEditorCopyPaste._DoPaste(editor, data, isDragDrop: true)</c>,
        /// тот поднимает <c>DataObject.Pasting</c> (IsDragDrop=true) на
        /// редакторе — и после фильтра вставляет ТОЛЬКО UnicodeText.
        /// Зовём _DoPaste напрямую (reflection): подмена OLE-пломбинга не
        /// нужна, а окно/визуальное дерево не требуются — без этого тест
        /// ловил бы крест-поточную флакающность общего STA-воркера.
        /// </summary>
        [Fact]
        public void Drop_OnRichTextBox_GoesThroughNotesPastingFilter()
        {
            string report = WpfTestHelper.RunOnSta(() =>
            {
                var rtb = new RichTextBox { Height = 150, Width = 300, AcceptsReturn = true };
                rtb.Document.Blocks.Clear();
                rtb.Document.Blocks.Add(new Paragraph(new Run("start")));

                int pastingCalls = 0;
                bool isDragDrop = false;
                DataObject.AddPastingHandler(rtb, (_, e) =>
                {
                    pastingCalls++;
                    isDragDrop = e.IsDragDrop;
                    SidebarControl.NotesPastingFilter(rtb, e);
                });

                // Payload «из Word»: богатые форматы + запасной plain text.
                var data = new DataObject();
                data.SetData(DataFormats.Rtf, @"{\rtf1\ansi\ansicpg1251 dropped \b 12141\b0  text}");
                data.SetData(DataFormats.Html, "<html><body><b>12141</b> text</body></html>");
                data.SetData(DataFormats.UnicodeText, "dropped 12141 text");

                // TextEditor и TextEditorCopyPaste — internal (PresentationFramework).
                var framework = typeof(RichTextBox).Assembly;
                var textEditor = framework
                    .GetType("System.Windows.Documents.TextEditor")!
                    .GetMethod("_GetTextEditor", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new object[] { rtb });
                bool pasted = (bool)framework
                    .GetType("System.Windows.Documents.TextEditorCopyPaste")!
                    .GetMethod("_DoPaste", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new[] { textEditor, (System.Windows.IDataObject)data, true })!;

                bool anyBold = false;
                foreach (var block in rtb.Document.Blocks)
                {
                    if (block is Paragraph p)
                        foreach (var inline in p.Inlines)
                            if (inline is Run r && r.FontWeight == FontWeights.Bold)
                                anyBold = true;
                }

                string text = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd)
                    .Text.Replace("\r", "|").Replace("\n", "|");

                return $"calls={pastingCalls} isDragDrop={isDragDrop} pasted={pasted} anyBold={anyBold} text='{text}'";
            }, 30_000);

            _out.WriteLine(report);
            Assert.StartsWith("calls=1 isDragDrop=True pasted=True", report);
            Assert.Contains("anyBold=False", report);
            Assert.Contains("dropped 12141 text", report);
        }

        /// <summary>
        /// Фильтр напрямую: у payload'а с богатыми форматами после обработки
        /// должен остаться ровно один формат — UnicodeText (и FormatToApply
        /// перевыбирается на него автоматически при присвоении DataObject).
        /// </summary>
        [Fact]
        public void NotesPastingFilter_StripsRichFormats_KeepsOnlyUnicodeText()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var data = new DataObject();
                data.SetData(DataFormats.Rtf, @"{\rtf1\ansi \b word\b0}");
                data.SetData(DataFormats.Html, "<html><body><b>word</b></body></html>");
                data.SetData(DataFormats.UnicodeText, "word");

                var e = CreatePastingArgs(data, isDragDrop: true, formatToApply: DataFormats.Rtf);
                Assert.Equal(DataFormats.Rtf, e.FormatToApply);

                SidebarControl.NotesPastingFilter(null!, e);

                Assert.False(e.CommandCancelled);
                Assert.Equal(DataFormats.UnicodeText, e.FormatToApply);
                Assert.Equal(new[] { DataFormats.UnicodeText }, ((DataObject)e.DataObject).GetFormats(autoConvert: false));
                Assert.Equal("word", (string)e.DataObject.GetData(DataFormats.UnicodeText));
            }, 30_000);
        }

        /// <summary>
        /// Payload без текста (картинка из браузера, файл из проводника) —
        /// операция отменяется, ничего не вставляется.
        /// </summary>
        [Fact]
        public void NotesPastingFilter_CancelsPayloadWithoutUnicodeText()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var data = new DataObject();
                data.SetData(DataFormats.Rtf, @"{\rtf1\ansi \b only rich\b0}");

                var e = CreatePastingArgs(data, isDragDrop: true, formatToApply: DataFormats.Rtf);

                SidebarControl.NotesPastingFilter(null!, e);

                Assert.True(e.CommandCancelled);
            }, 30_000);
        }

        /// <summary>
        /// Проводка в контроле: барьер ровно один и сидит на Pasting (общий для
        /// Ctrl+V и drop), отдельного обработчика Drop быть не должно — он
        /// превратил бы родной перенос выделения внутри редактора во вставку
        /// копии. Курсорный гейт для payload'ов без текста — на PreviewDragOver.
        /// </summary>
        [Fact]
        public void SidebarControl_RoutesPastingAndDragOverThroughTheSingleFilter()
        {
            string code = File.ReadAllText(Path.Combine(
                RepoRoot(), "MosquitoNetCalculator", "Controls", "SidebarControl.xaml.cs"));

            Assert.Contains("DataObject.AddPastingHandler(RtfNotes, NotesPastingFilter)", code, StringComparison.Ordinal);
            Assert.Contains("PreviewDragOverEvent", code, StringComparison.Ordinal);

            Assert.False(Regex.IsMatch(code, @"Drop=|AddHandler\(DragDrop\.DropEvent|AddDropHandler"),
                "Отдельный обработчик Drop запрещён: framework сам поднимает DataObject.Pasting на drop, " +
                "а локальный Drop-хендлер сломал бы родной перенос выделения внутри редактора.");
        }

        /// <summary>DataObjectPastingEventArgs конструируется только публичным ctor.</summary>
        private static DataObjectPastingEventArgs CreatePastingArgs(DataObject data, bool isDragDrop, string formatToApply)
            => (DataObjectPastingEventArgs)typeof(DataObjectPastingEventArgs)
                .GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(IDataObject), typeof(bool), typeof(string) },
                    null)!
                .Invoke(new object[] { data, isDragDrop, formatToApply });
    }
}
