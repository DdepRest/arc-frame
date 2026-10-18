using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Хранимая разметка примечаний ⇄ документ редактора (v3.55).
    ///
    /// Пользователь больше не видит маркеров: редактор показывает сам формат,
    /// а разметка остаётся ЕДИНСТВЕННЫМ хранимым форматом (печать КП, PDF,
    /// заказ, старые заказы). Поэтому конвертация обязана быть обратимой —
    /// эти тесты держат именно round-trip, а не отдельные фрагменты.
    /// </summary>
    public class NotesMarkupWriterTests
    {
        /// <summary>Сериализация «как в приложении»: цветом темы служит цвет документа.</summary>
        private static string Markup(FlowDocument document)
            => NotesMarkupWriter.ToMarkup(document, document.Foreground);

        private static FlowDocument Build(string markup) => NotesDocumentBuilder.Build(markup);

        private static void RoundTrip(string markup)
        {
            WpfTestHelper.RunOnSta(() =>
            {
                string actual = Markup(Build(markup));
                Assert.Equal(markup, actual);
            });
        }

        // ─── Основное: разметка → документ → разметка ────────────────────

        [Fact]
        public void RoundTrip_PlainText_KeepsTextWithoutTags() => RoundTrip("Позвонить за час");

        [Fact]
        public void RoundTrip_Bold_KeepsMarkers() => RoundTrip("**важно**");

        [Fact]
        public void RoundTrip_Italic_KeepsMarkers() => RoundTrip("*курсив*");

        [Fact]
        public void RoundTrip_Color_KeepsHexTag() => RoundTrip("[color=#D32F2F]красный[/color]");

        [Fact]
        public void RoundTrip_List_KeepsBulletMarkers() => RoundTrip("- первый пункт\n- второй пункт");

        [Fact]
        public void RoundTrip_MixedFormatting_KeepsEverySpan()
            => RoundTrip("**жирный** и *курсив*, и [color=#1976D2]синий[/color]");

        [Fact]
        public void RoundTrip_NestedBoldItalic_KeepsBothFlags()
            => RoundTrip("**жирный *и курсив* вместе**");

        [Fact]
        public void RoundTrip_BlankLine_IsPreserved() => RoundTrip("первая\n\nвторая");

        [Fact]
        public void RoundTrip_PartialBoldInALine_KeepsSplit()
            => RoundTrip("до **важно** после");

        [Fact]
        public void RoundTrip_EmptyMarkup_ReturnsEmpty()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                Assert.Equal(string.Empty, Markup(Build(string.Empty)));
                Assert.Empty(Build(string.Empty).Blocks);
            });
        }

        /// <summary>
        /// Именованный цвет старого заказа при сохранении становится hex —
        /// синтаксис тот же, печать и старые заказы читаются как раньше.
        /// </summary>
        [Fact]
        public void RoundTrip_NamedColor_NormalizesToHex()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                Assert.Equal("[color=#FF0000]старый[/color]", Markup(Build("[color=red]старый[/color]")));
            });
        }

        // ─── То, что делает сам редактор ────────────────────────────────

        /// <summary>
        /// Кнопка «Ж» применяет форматирование к выделению: WPF ставит свойство на
        /// элементы диапазона. Writer обязан увидеть это в документе.
        /// </summary>
        [Fact]
        public void ToMarkup_BoldAppliedByEditor_IsSerialized()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var document = Build("привет");
                new TextRange(document.ContentStart, document.ContentEnd)
                    .ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);

                Assert.Equal("**привет**", Markup(document));
            });
        }

        [Fact]
        public void ToMarkup_ItalicAppliedByEditor_IsSerialized()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var document = Build("привет");
                new TextRange(document.ContentStart, document.ContentEnd)
                    .ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Italic);

                Assert.Equal("*привет*", Markup(document));
            });
        }

        [Fact]
        public void ToMarkup_ColorAppliedByEditor_IsSerializedAsHex()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var document = Build("привет");
                new TextRange(document.ContentStart, document.ContentEnd)
                    .ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Red));

                Assert.Equal("[color=#FF0000]привет[/color]", Markup(document));
            });
        }

        /// <summary>
        /// Снятие форматирования («✕») должно давать чистый текст: иначе
        /// пользователь снимает жирный, а в заказ уезжает прежняя разметка.
        /// </summary>
        [Fact]
        public void ToMarkup_FormattingClearedByEditor_LeavesPlainText()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var document = Build("**жирный** [color=#FF0000]красный[/color]");
                new TextRange(document.ContentStart, document.ContentEnd).ClearAllProperties();

                Assert.Equal("жирный красный", Markup(document));
            });
        }

        /// <summary>
        /// Пункт списка — настоящий WPF-List, иначе команда «Список» (ToggleBullets)
        /// и writer говорили бы на разных языках.
        /// </summary>
        [Fact]
        public void Build_ListItem_BecomesRealWpfList()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var document = Build("- пункт");

                var list = Assert.IsType<List>(Assert.Single(document.Blocks));
                Assert.Equal(TextMarkerStyle.Disc, list.MarkerStyle);
                Assert.Single(list.ListItems);
            });
        }

        [Fact]
        public void Build_PlainLine_StaysParagraph()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var document = Build("строка");
                Assert.IsType<Paragraph>(Assert.Single(document.Blocks));
            });
        }

        /// <summary>
        /// Редактор не «пришпиливает» вес и начертание к обычному тексту: они
        /// наследуются от редактора, поэтому цвет темы и размер приходят сверху,
        /// а команды форматирования не воюют с локальными значениями.
        /// </summary>
        [Fact]
        public void Build_PlainRun_DoesNotPinFormatting()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var document = Build("обычный текст");
                var run = (Run)document.Blocks.OfType<Paragraph>().Single().Inlines.Single();

                Assert.Equal(DependencyProperty.UnsetValue, run.ReadLocalValue(TextElement.FontWeightProperty));
                Assert.Equal(DependencyProperty.UnsetValue, run.ReadLocalValue(TextElement.FontStyleProperty));
                Assert.Equal(DependencyProperty.UnsetValue, run.ReadLocalValue(TextElement.ForegroundProperty));
            });
        }

        /// <summary>
        /// Печатный слой по-прежнему получает явные атрибуты: страница не
        /// переключается темой, ей неоткуда наследовать (см. NotesRendererTests).
        /// </summary>
        [Fact]
        public void CreateRun_PrintPath_KeepsExplicitFormatting()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var run = NotesRenderer.CreateRun(NotesFormatter.Parse("текст").Single().Segments.Single());

                Assert.Equal(FontWeights.Normal, run.FontWeight);
                Assert.Equal(FontStyles.Normal, run.FontStyle);
            });
        }

        /// <summary>
        /// Несколько Run'ов с одинаковым форматом (WPF дробит текст как угодно)
        /// сливаются в одну пару маркеров, а не в мусор «**a****b**».
        /// </summary>
        [Fact]
        public void ToMarkup_AdjacentRunsWithSameFormatting_ShareOneMarkerPair()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var document = new FlowDocument { PagePadding = new Thickness(0) };
                var paragraph = new Paragraph();
                paragraph.Inlines.Add(new Run("жир") { FontWeight = FontWeights.Bold });
                paragraph.Inlines.Add(new Run("ный") { FontWeight = FontWeights.Bold });
                document.Blocks.Add(paragraph);

                Assert.Equal("**жирный**", Markup(document));
            });
        }
    }
}
