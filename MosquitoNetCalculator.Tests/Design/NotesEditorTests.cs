using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Страж «примечания без видимого кода» (v3.55).
    ///
    /// Жалоба владельца: нажал «Ж» — в поле появилось «**12141**», и тот же код
    /// уезжал в отдельный предпросмотр. Теперь поле — редактор с форматированием,
    /// а разметка живёт только в хранимой строке (печать КП, PDF, заказ).
    ///
    /// Страж дешёвый, но держит именно то, что легко откатить назад: поле
    /// (<c>RichTextBox</c>), отсутствие предпросмотра и отсутствие маркеров в
    /// строковых литералах тулбара.
    /// </summary>
    public class NotesEditorTests
    {
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

        private static string AppDir => Path.Combine(RepoRoot(), "MosquitoNetCalculator");

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(AppDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        [Fact]
        public void NotesField_IsAWysiwygEditor_NotAMarkupPreview()
        {
            string xaml = Read("Controls/SidebarControl.xaml");

            Assert.Contains("RichTextBox x:Name=\"RtfNotes\"", xaml, StringComparison.Ordinal);

            // Отдельный предпросмотр был вторым местом, где показывался «код»
            // (и он же «рвался»): теперь его роль играет сам редактор.
            Assert.DoesNotContain("FlowDocumentScrollViewer", xaml);
            Assert.DoesNotContain("NotesPreview", xaml);
            Assert.DoesNotContain("Предпросмотр:", xaml);
            Assert.DoesNotContain("TxtNotes", xaml);
        }

        [Fact]
        public void NotesToolbar_DoesNotWriteMarkupIntoTheText()
        {
            string code = Read("Controls/SidebarControl.xaml.cs");

            // Кнопки применяют форматирование к выделению, а не вставляют маркеры:
            // в строковых литералах их быть не должно (в комментариях — можно).
            Assert.DoesNotContain("\"**\"", code);
            Assert.DoesNotContain("\"*\"", code);
            Assert.DoesNotContain("\"[color=", code);
            Assert.DoesNotContain("WrapSelection", code);

            Assert.Contains("EditingCommands.ToggleBold", code, StringComparison.Ordinal);
            Assert.Contains("EditingCommands.ToggleItalic", code, StringComparison.Ordinal);
            Assert.Contains("EditingCommands.ToggleBullets", code, StringComparison.Ordinal);
        }

        /// <summary>
        /// Хранимый формат — по-прежнему разметка: иначе молча сломались бы старые
        /// заказы, печать и PDF. Редактор только конвертирует её в обе стороны.
        /// </summary>
        [Fact]
        public void NotesEditor_KeepsTheStoredMarkupFormatAsTheOnlyPersistence()
        {
            string code = Read("Controls/SidebarControl.xaml.cs");

            Assert.Contains("NotesDocumentBuilder.Build", code, StringComparison.Ordinal);
            Assert.Contains("NotesMarkupWriter.ToMarkup", code, StringComparison.Ordinal);

            string print = Read("Services/FlowDocumentBuilder.cs");
            Assert.Contains("NotesFormatter.Parse(clientInfo.Notes)", print, StringComparison.Ordinal);

            string pdf = Read("Services/PdfExportService.cs");
            Assert.Contains("NotesFormatter.Parse(clientInfo.Notes)", pdf, StringComparison.Ordinal);
        }

        /// <summary>
        /// Форматтеры примечаний печати и экрана обязаны остаться одним источником
        /// истины: второй парсер разошёлся бы с первым на первой же правке синтаксиса.
        /// </summary>
        [Fact]
        public void NotesParsers_AreStillSingleSourceOfTruth()
        {
            string builder = Read("Services/NotesDocumentBuilder.cs");
            Assert.Contains("NotesFormatter.Parse(markup)", builder, StringComparison.Ordinal);

            string writer = Read("Services/NotesMarkupWriter.cs");
            Assert.Matches(new Regex("class NotesMarkupWriter"), writer);
        }
    }
}
