using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Порядок копий в предпросмотре КП: сначала ЧИСТЫЙ экземпляр заказчика,
    /// затем — отдельным блоком — копия «В производство» (та же семантика, что
    /// в физической печати FixedDocumentBuilder и в PdfExportService).
    /// Регрессия: раньше штамп накладывался на ПЕРВУЮ страницу чистого КП, и в
    /// предпросмотре «В ПРОИЗВОДСТВО» выглядело идущим первым.
    /// </summary>
    [Collection("WPF_UI")]
    public class PrintPreviewProductionOrderTests
    {
        private static FlowDocument MakeSource(int paragraphs = 2)
        {
            var doc = new FlowDocument
            {
                PageWidth = 793.7,
                PageHeight = 1122.5,
                PagePadding = new Thickness(30)
            };
            for (int i = 0; i < paragraphs; i++)
                doc.Blocks.Add(new Paragraph(new Run($"Строка КП {i}")));
            return doc;
        }

        [Fact]
        public void BuildPreviewDocument_ProductionOn_AppendsProductionBlock_AfterCleanCopy()
        {
            var (previewBlocks, sourceBlocks, cleanPages, productionStart, appendedBreaksPage, sourceKept)
                = WpfTestHelper.RunOnSta(() =>
                {
                    var source = MakeSource();
                    int before = source.Blocks.Count;

                    var preview = PrintPreviewControl.BuildPreviewDocument(
                        source, includeProductionCopy: true, out int cleanPages, out int productionStart);

                    // Первый блок второго экземпляра — идёт сразу после чистого КП
                    // и обязан начинаться с НОВОГО листа (не прилипать к чистому).
                    var appended = preview.Blocks.Skip(before).First();
                    bool breaks = appended.BreakPageBefore;

                    // Исходный документ печати не трогаем: его блоки не «переехали».
                    bool sourceKept = source.Blocks.Count == before && !ReferenceEquals(preview, source);
                    return (preview.Blocks.Count, before, cleanPages, productionStart, breaks, sourceKept);
                });

            // Чистый экземпляр + производственный экземпляр.
            Assert.Equal(sourceBlocks * 2, previewBlocks);
            Assert.True(cleanPages >= 1, "Чистый экземпляр КП должен пагинироваться хотя бы в одну страницу.");
            // Штамп ждёт на ПЕРВОМ листе производственного блока — ПОСЛЕ чистого КП.
            Assert.Equal(cleanPages + 1, productionStart);
            Assert.True(appendedBreaksPage, "Производственный блок должен начинаться с нового листа.");
            Assert.True(sourceKept, "Исходный документ печати не должен меняться при сборке предпросмотра.");
        }

        [Fact]
        public void BuildPreviewDocument_ProductionOff_KeepsCleanCopyOnly()
        {
            var (previewBlocks, sourceBlocks, cleanPages, productionStart)
                = WpfTestHelper.RunOnSta(() =>
                {
                    var source = MakeSource();
                    int before = source.Blocks.Count;

                    var preview = PrintPreviewControl.BuildPreviewDocument(
                        source, includeProductionCopy: false, out int cleanPages, out int productionStart);

                    return (preview.Blocks.Count, before, cleanPages, productionStart);
                });

            // Без флажка — только чистое КП, без второго экземпляра.
            Assert.Equal(sourceBlocks, previewBlocks);
            Assert.True(cleanPages >= 1);
            // 0 = блока нет, штамп в предпросмотре не показывается.
            Assert.Equal(0, productionStart);
        }

        // ─── Сквозной путь контролов: Initialize + флажок → документ предпросмотра ───

        [Fact]
        public void Control_ProductionCheckboxOn_ShowsCleanCopyFirst_ProductionBlockSecond()
        {
            FlowDocument? preview = null;
            int sourceBlocks = 0, cleanPages = 0, productionStart = 0;
            bool appendedBreaksPage = false;
            bool previewIsClone = false;

            TestAppThemes.RunOnSta(() =>
            {
                var control = new PrintPreviewControl();
                var source = MakeSource();
                sourceBlocks = source.Blocks.Count;

                control.Initialize(source, new PrintService(), "1-1",
                    new PrintSettings { IncludeProductionCopy = false, Copies = 1 });

                // Включаем флажок — тот же путь, что у RestoreSettings/пользователя.
                control.ProductionCopyCheck.IsChecked = true;

                preview = control.DocumentReader.Document as FlowDocument;
                previewIsClone = preview != null && !ReferenceEquals(preview, source);

                (cleanPages, productionStart) = ReadPreviewOrder(control);

                appendedBreaksPage = preview != null
                    && preview.Blocks.Skip(sourceBlocks).Any()
                    && preview.Blocks.Skip(sourceBlocks).First().BreakPageBefore;
            });

            Assert.NotNull(preview);
            Assert.True(previewIsClone, "Предпросмотр обязан строиться из клона, а не из документа печати.");
            // Чистый экземпляр КП первым, производственный — вторым блоком.
            Assert.Equal(sourceBlocks * 2, preview!.Blocks.Count);
            Assert.Equal(cleanPages + 1, productionStart);
            Assert.True(appendedBreaksPage, "Производственный блок должен начинаться с нового листа.");
        }

        [Fact]
        public void Control_ProductionCheckboxOff_ReturnsToCleanCopyOnly()
        {
            FlowDocument? preview = null;
            int sourceBlocks = 0, cleanPages = -1, productionStart = -1;

            TestAppThemes.RunOnSta(() =>
            {
                var control = new PrintPreviewControl();
                var source = MakeSource();
                sourceBlocks = source.Blocks.Count;

                control.Initialize(source, new PrintService(), "1-1",
                    new PrintSettings { IncludeProductionCopy = false, Copies = 1 });

                // WPF поднимает Unchecked ТОЛЬКО при изменении значения, поэтому
                // сначала включаем флажок (пересборка со вторым экземпляром),
                // затем снимаем — control обязан вернуться к одному чистому КП.
                control.ProductionCopyCheck.IsChecked = true;
                control.ProductionCopyCheck.IsChecked = false;

                preview = control.DocumentReader.Document as FlowDocument;
                (cleanPages, productionStart) = ReadPreviewOrder(control);
            });

            Assert.NotNull(preview);
            // Второй экземпляр убран — снова только чистое КП, штампа негде показывать.
            Assert.Equal(sourceBlocks, preview!.Blocks.Count);
            Assert.Equal(0, productionStart);
            Assert.True(cleanPages >= 1);
        }

        private static (int cleanPages, int productionStart) ReadPreviewOrder(PrintPreviewControl control)
        {
            var type = typeof(PrintPreviewControl);
            int clean = (int)type.GetField("_cleanPageCount", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(control)!;
            int start = (int)type.GetField("_productionStartPage", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(control)!;
            return (clean, start);
        }

        // ─── Навигация по страницам: кнопки «<» / «>» при 2 страницах ───

        [Fact]
        public void PageNavigation_ButtonsEnableAndSwitchPages()
        {
            string loadedIndicator = "", afterNext = "", afterPrev = "";
            bool prevEnabled = true, nextEnabled = false;
            int pageAfterNext = -1, pageAfterPrev = -1;
            Visibility stampOnPage1 = Visibility.Visible, stampOnPage2 = Visibility.Collapsed;

            TestAppThemes.RunOnSta(() =>
            {
                var window = new Window
                {
                    Width = 1400,
                    Height = 900,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                };
                var control = new PrintPreviewControl();
                window.Content = control;
                try
                {
                    // Чистое КП (1 стр.) + копия «В производство» (1 стр.) = 2 страницы.
                    control.Initialize(MakeSource(1), new PrintService(), "1-1",
                        new PrintSettings { IncludeProductionCopy = true, Copies = 1, Collated = true });
                    window.Show();
                    Pump(700);   // Loaded + отложенные колбэки + пагинация viewer'а

                    loadedIndicator = control.PageIndicator.Text;
                    prevEnabled = control.PrevPageBtn.IsEnabled;
                    nextEnabled = control.NextPageBtn.IsEnabled;
                    stampOnPage1 = control.StampOverlay.Visibility;

                    control.NextPageBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump(400);
                    afterNext = control.PageIndicator.Text;
                    pageAfterNext = control.DocumentReader.MasterPageNumber;
                    stampOnPage2 = control.StampOverlay.Visibility;

                    control.PrevPageBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump(400);
                    afterPrev = control.PageIndicator.Text;
                    pageAfterPrev = control.DocumentReader.MasterPageNumber;
                }
                finally
                {
                    window.Close();
                    Pump(200);
                }
            });

            // Состояние после загрузки: 2 страницы, назад некуда, вперёд — можно.
            Assert.Equal("1 / 2", loadedIndicator);
            Assert.False(prevEnabled, "На первой странице «<» должен быть неактивен.");
            Assert.True(nextEnabled, "При индикаторе 1/2 кнопка «>» обязана быть активной.");

            // Клик «>» — уходим на страницу производственного блока, штамп там.
            Assert.Equal(2, pageAfterNext);
            Assert.Equal("2 / 2", afterNext);
            Assert.Equal(Visibility.Visible, stampOnPage2);
            Assert.Equal(Visibility.Collapsed, stampOnPage1);

            // Клик «<» — возврат к чистому КП, штамп гаснет.
            Assert.Equal(1, pageAfterPrev);
            Assert.Equal("1 / 2", afterPrev);
        }

        [Fact]
        public void PageNavigation_ToggleProductionWhileOpen_ButtonsAndStampFollow()
        {
            string initial = "", afterToggleOn = "", afterNext = "", afterPrev = "", afterToggleOff = "";
            bool nextAfterLoad = false, nextAfterToggle = false, prevAfterToggle = false;
            bool nextAfterReturn = false;
            bool stampLoaded = false;
            string afterSecondNext = "";
            int pageAfterSecondNext = -1;
            int pageAfterNext = -1, pageAfterPrev = -1;

            TestAppThemes.RunOnSta(() =>
            {
                var window = new Window
                {
                    Width = 1400,
                    Height = 900,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                };
                var control = new PrintPreviewControl();
                window.Content = control;
                try
                {
                    control.Initialize(MakeSource(1), new PrintService(), "1-1",
                        new PrintSettings { IncludeProductionCopy = false, Copies = 1, Collated = true });
                    window.Show();
                    Pump(700);

                    initial = control.PageIndicator.Text;
                    nextAfterLoad = control.NextPageBtn.IsEnabled;

                    // Пользователь включает флажок «В производство» на лету.
                    control.ProductionCopyCheck.IsChecked = true;
                    Pump(700);
                    afterToggleOn = control.PageIndicator.Text;
                    nextAfterToggle = control.NextPageBtn.IsEnabled;
                    prevAfterToggle = control.PrevPageBtn.IsEnabled;

                    // Переход на производственный лист: штамп и картинка должны быть.
                    control.NextPageBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump(400);
                    afterNext = control.PageIndicator.Text;
                    pageAfterNext = control.DocumentReader.MasterPageNumber;
                    stampLoaded = control.StampImage.Source != null;

                    // Обратно — и кнопка «>» снова активна.
                    control.PrevPageBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump(400);
                    afterPrev = control.PageIndicator.Text;
                    pageAfterPrev = control.DocumentReader.MasterPageNumber;
                    nextAfterReturn = control.NextPageBtn.IsEnabled;

                    // Повторный «вперёд» после возврата — до фикса кнопка висела
                    // неактивной, и реальный клик мышью её бы не обработал.
                    control.NextPageBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump(400);
                    afterSecondNext = control.PageIndicator.Text;
                    pageAfterSecondNext = control.DocumentReader.MasterPageNumber;

                    // Снимаем флажок — предпросмотр возвращается к одному листу.
                    control.ProductionCopyCheck.IsChecked = false;
                    Pump(700);
                    afterToggleOff = control.PageIndicator.Text;
                }
                finally
                {
                    window.Close();
                    Pump(200);
                }
            });

            Assert.Equal("1 / 1", initial);
            Assert.False(nextAfterLoad, "Без производственной копии всего 1 страница — «>» неактивна.");

            // Флажок включён: 2 страницы, «>» активна, «<» всё ещё неактивна.
            Assert.Equal("1 / 2", afterToggleOn);
            Assert.True(nextAfterToggle, "После включения копии «В производство» кнопка «>» обязана включиться.");
            Assert.False(prevAfterToggle, "На первой странице «<» должна быть неактивна.");

            Assert.Equal(2, pageAfterNext);
            Assert.Equal("2 / 2", afterNext);
            Assert.True(stampLoaded, "Картинка штампа «В ПРОИЗВОДСТВО» не загрузилась на странице 2.");

            Assert.Equal(1, pageAfterPrev);
            Assert.Equal("1 / 2", afterPrev);
            Assert.True(nextAfterReturn, "После возврата на страницу 1 кнопка «>» должна снова включиться.");
            Assert.Equal(2, pageAfterSecondNext);
            Assert.Equal("2 / 2", afterSecondNext);

            // Флажок снят: снова одна страница.
            Assert.Equal("1 / 1", afterToggleOff);
        }

        /// <summary>Прокачивает dispatcher: Loaded/отложенные колбэки/пагинация.</summary>
        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        // ─── Source-scan: штамп и пересборка привязаны к производственному блоку ───

        [Fact]
        public void Preview_GatesStampToProductionBlock_NotToFirstPage()
        {
            string code = ReadPreviewSource();

            int gateStart = code.IndexOf("private void GateStampToFirstPage", StringComparison.Ordinal);
            Assert.True(gateStart >= 0, "GateStampToFirstPage() not found in PrintPreviewControl.xaml.cs.");
            string gateWindow = code.Substring(gateStart, Math.Min(700, code.Length - gateStart));

            Assert.Contains("_productionStartPage > 0 && currentPage == _productionStartPage", gateWindow);
            // Старое поведение (штамп на первой странице чистого КП) — регрессия.
            Assert.DoesNotContain("currentPage == 1", gateWindow);
        }

        [Fact]
        public void Preview_RebuildsDocument_WhenProductionCheckboxToggles()
        {
            string code = ReadPreviewSource();

            int handlerStart = code.IndexOf("private void ProductionCopyCheck_Changed", StringComparison.Ordinal);
            Assert.True(handlerStart >= 0, "ProductionCopyCheck_Changed() not found in PrintPreviewControl.xaml.cs.");
            string handlerWindow = code.Substring(handlerStart, Math.Min(700, code.Length - handlerStart));
            Assert.Contains("RebuildPreviewDocument(ProductionCopyCheck.IsChecked == true)", handlerWindow);

            // Страницы производственного блока не гасятся при выборе диапазона:
            // блок печатается целиком вторым и в печати, и в PDF.
            int rebuildStart = code.IndexOf("private void RebuildPreviewDocument", StringComparison.Ordinal);
            Assert.True(rebuildStart >= 0, "RebuildPreviewDocument() not found in PrintPreviewControl.xaml.cs.");
            Assert.Contains("_productionStartPage > 0 && currentPage >= _productionStartPage", code);
        }

        // ─── Реальный документ КП (FlowDocumentBuilder): склонирования через XamlWriter ───
        // Регрессия «задача не выполнена»: DeepCloneDocument при любом сбое XamlWriter
        // молча возвращает ИСХОДНЫЙ документ; guard !ReferenceEquals(clean, source)
        // тогда тихо пропускает блок «В производство», и предпросмотр показывает
        // только чистое КП (как на скриншоте бага — 4 стр., штамп на первой).

        [Fact]
        public void BuildPreviewDocument_OnRealQuotationDocument_AppendsProductionBlock()
        {
            var (previewBlocks, sourceBlocks, cleanPages, productionStart) = WpfTestHelper.RunOnSta(() =>
            {
                var items = new System.Collections.Generic.List<MosquitoNetCalculator.Models.OrderItem>
                {
                    new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 },
                    new() { Name = "Откос", Color = "Белый", Width = 1290, Height = 1750, Quantity = 1, Price = 5724, Total = 5724 },
                };
                FlowDocument source = new PrintService().BuildFlowDocument(
                    items, new MosquitoNetCalculator.Models.ClientInfo
                    {
                        ContractNumber = "2-56",
                        ClientAddress = "Пушкинская 13/21", ClientPhone = "+79493684016",
                    }, 7524, "Семь тысяч пятьсот двадцать четыре рубля 00 копеек")!;
                Assert.NotNull(source);

                int before = source.Blocks.Count;
                var preview = PrintPreviewControl.BuildPreviewDocument(
                    source, includeProductionCopy: true, out int cleanPages, out int productionStart);
                return (preview.Blocks.Count, before, cleanPages, productionStart);
            });

            // Блок «В производство» обязан появиться в предпросмотре на реальном КП.
            Assert.True(previewBlocks > sourceBlocks,
                $"На реальном документе КП предпросмотр остался одним экземпляром " +
                $"({previewBlocks} блоков) — блок «В производство» не добавился. " +
                "Похоже на тихий сбой DeepCloneDocument (fallback на исходный документ).");
            Assert.True(cleanPages >= 1);
            Assert.Equal(cleanPages + 1, productionStart);
        }

        // ─── Рассинхрон «флажок ↔ документ» при повторном открытии предпросмотра ───
        // Регрессия «ужасных багов»: контрол живёт в постоянно видимом оверлее,
        // Loaded поднимается один раз за сессию, поэтому RestoreSettings() из
        // OnLoaded при ПОВТОРНОМ Initialize не выполнялся. Документ перестраивался
        // по persisted-галочке (IncludeProductionCopy=true), а флажок в панели
        // оставался выключенным: штамп не показывался нигде, а печать/PDF, собрав
        // настройки по флажку, выводили один экземпляр вместо показанных двух.

        [Fact]
        public void Control_SecondInitialize_SyncsCheckboxAndDocumentWithSettings()
        {
            bool checkboxOn = false;
            string indicator = "";
            bool stampOnPage2 = false, cleanPage1NoStamp = true;

            TestAppThemes.RunOnSta(() =>
            {
                var window = new Window
                {
                    Width = 1400,
                    Height = 900,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                };
                var control = new PrintPreviewControl();
                window.Content = control;
                try
                {
                    // Первое открытие: галочка выключена.
                    control.Initialize(MakeSource(1), new PrintService(), "1-1",
                        new PrintSettings { IncludeProductionCopy = false, Copies = 1 });
                    window.Show();
                    Pump(600);

                    // Второе открытие ТОГО ЖЕ контрола: persisted-настройка теперь
                    // true (как у владельца на скриншоте бага).
                    control.Initialize(MakeSource(1), new PrintService(), "1-1",
                        new PrintSettings { IncludeProductionCopy = true, Copies = 1 });
                    Pump(800);

                    checkboxOn = control.ProductionCopyCheck.IsChecked == true;
                    indicator = control.PageIndicator.Text;
                    cleanPage1NoStamp = control.StampOverlay.Visibility == Visibility.Collapsed;

                    // Перейти на страницу 2 — производственный блок со штампом.
                    control.NextPageBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump(500);
                    stampOnPage2 = control.StampOverlay.Visibility == Visibility.Visible;
                }
                finally
                {
                    window.Close();
                    Pump(200);
                }
            });

            Assert.True(checkboxOn,
                "Флажок «В производство» обязан синхронизироваться с настройками при каждом Initialize — " +
                "иначе документ собран с копией, а панель (штамп, печать, PDF) считает, что её нет.");
            Assert.Equal("1 / 2", indicator);
            // Страница 1 — чистое КП без штампа; страница 2 — блок «В ПРОИЗВОДСТВО».
            Assert.True(cleanPage1NoStamp, "На первой странице (чистое КП) штампа быть не должно.");
            Assert.True(stampOnPage2, "На странице производственного блока должен быть штамп.");
        }

        [Fact]
        public void BuildPreviewDocument_FallbackWithFactory_WhenCloningFails()
        {
            var (previewBlocks, sourceBlocks, cleanPages, productionStart, appendedBreaksPage)
                = WpfTestHelper.RunOnSta(() =>
                {
                    // Source doc that represents the document
                    var source = MakeSource(2);
                    int before = source.Blocks.Count;

                    // Suppose source cannot be cloned by XamlWriter (or factory provides fresh document)
                    // We simulate factory producing distinct fresh documents:
                    var preview = PrintPreviewControl.BuildPreviewDocument(
                        source,
                        includeProductionCopy: true,
                        out int cleanPages,
                        out int productionStart,
                        documentFactory: () => MakeSource(2));

                    var appended = preview.Blocks.Skip(before).First();
                    bool breaks = appended.BreakPageBefore;

                    return (preview.Blocks.Count, before, cleanPages, productionStart, breaks);
                });

            Assert.Equal(sourceBlocks * 2, previewBlocks);
            Assert.True(cleanPages >= 1);
            Assert.Equal(cleanPages + 1, productionStart);
            Assert.True(appendedBreaksPage);
        }

        [Fact]
        public void Zoom_ScalesDocumentReader_AndStampSynchronously()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var window = new Window
                {
                    Width = 1400,
                    Height = 900,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                };
                var control = new PrintPreviewControl();
                window.Content = control;
                try
                {
                    control.Initialize(MakeSource(1), new PrintService(), "1-1",
                        new PrintSettings { IncludeProductionCopy = true, Copies = 1 });
                    window.Show();
                    Pump(500);

                    // Go to page 2 (production block)
                    control.NextPageBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump(200);

                    Assert.Equal(Visibility.Visible, control.StampOverlay.Visibility);

                    var applyZoomMethod = typeof(PrintPreviewControl).GetMethod("ApplyZoom", BindingFlags.Instance | BindingFlags.NonPublic)!;

                    foreach (double zoom in new[] { 0.5, 0.77, 1.0, 1.5, 2.0 })
                    {
                        applyZoomMethod.Invoke(control, new object[] { zoom });
                        Pump(100);

                        // DocumentReader.Zoom is in percentage (zoom * 100)
                        Assert.Equal(zoom * 100, control.DocumentReader.Zoom, 1);

                        // Stamp width/height are scaled synchronously with zoom
                        double expectedWidth = ProductionStampImage.WidthDip * zoom;
                        double expectedHeight = ProductionStampImage.HeightDip * zoom;
                        Assert.Equal(expectedWidth, control.StampImage.Width, 1);
                        Assert.Equal(expectedHeight, control.StampImage.Height, 1);

                        // Margins also scale with zoom
                        double expectedLeft = ProductionStampImage.LeftOffsetDip * zoom;
                        double expectedTop = ProductionStampImage.TopOffsetDip * zoom;
                        Assert.Equal(expectedLeft, control.StampImage.Margin.Left, 1);
                        Assert.Equal(expectedTop, control.StampImage.Margin.Top, 1);
                    }
                }
                finally
                {
                    window.Close();
                    Pump(200);
                }
            });
        }

        [Fact]
        public void TestMouseWheelInteraction()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var window = new Window
                {
                    Width = 800,
                    Height = 600,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                };
                var control = new PrintPreviewControl();
                window.Content = control;
                try
                {
                    control.Initialize(MakeSource(20), new PrintService(), "1-1",
                        new PrintSettings { IncludeProductionCopy = true, Copies = 1 });
                    window.Show();
                    Pump(500);

                    Assert.Equal(1, control.DocumentReader.MasterPageNumber);
                    Assert.True(control.DocumentReader.PageCount >= 2);
                    Assert.True(control.PreviewScroller.ScrollableHeight > 0);
                    Assert.Equal(0, control.PreviewScroller.VerticalOffset);

                    // 1. Roll wheel down on PreviewScroller: page should stay 1, vertical offset should increase
                    var wheelDown1 = new MouseWheelEventArgs(InputManager.Current.PrimaryMouseDevice, Environment.TickCount, -120)
                    {
                        RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    };
                    control.PreviewScroller.RaiseEvent(wheelDown1);
                    Pump(100);

                    Assert.True(wheelDown1.Handled);
                    Assert.Equal(1, control.DocumentReader.MasterPageNumber);
                    Assert.True(control.PreviewScroller.VerticalOffset > 0, "Vertical offset must scroll down on mouse wheel");

                    // 2. Scroll all the way to the bottom of Page 1
                    control.PreviewScroller.ScrollToBottom();
                    Pump(100);
                    double bottomOffset = control.PreviewScroller.VerticalOffset;
                    Assert.True(bottomOffset >= control.PreviewScroller.ScrollableHeight - 1);
                    Assert.Equal(1, control.DocumentReader.MasterPageNumber);

                    // 3. Roll wheel down while at the bottom of Page 1: should transition to Page 2 at TOP (offset 0)
                    // Wait past cooldown
                    System.Threading.Thread.Sleep(250);
                    var wheelDownToNext = new MouseWheelEventArgs(InputManager.Current.PrimaryMouseDevice, Environment.TickCount, -120)
                    {
                        RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    };
                    control.PreviewScroller.RaiseEvent(wheelDownToNext);
                    Pump(200);

                    Assert.Equal(2, control.DocumentReader.MasterPageNumber);
                    Assert.Equal(0, control.PreviewScroller.VerticalOffset);

                    // 4. Roll wheel up while at the top of Page 2: should transition to Page 1 at the bottom
                    System.Threading.Thread.Sleep(250);
                    var wheelUpToPrev = new MouseWheelEventArgs(InputManager.Current.PrimaryMouseDevice, Environment.TickCount, 120)
                    {
                        RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    };
                    control.PreviewScroller.RaiseEvent(wheelUpToPrev);
                    Pump(200);

                    Assert.Equal(1, control.DocumentReader.MasterPageNumber);
                    Assert.True(control.PreviewScroller.VerticalOffset > 0, "Returning to prev page from top should land at bottom of prev page");

                    // 5. Button NextPageBtn click: goes to Page 2 at TOP (offset 0)
                    control.NextPageBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump(100);
                    Assert.Equal(2, control.DocumentReader.MasterPageNumber);
                    Assert.Equal(0, control.PreviewScroller.VerticalOffset);

                    // 6. Button PrevPageBtn click: goes to Page 1 at TOP (offset 0)
                    control.PrevPageBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump(100);
                    Assert.Equal(1, control.DocumentReader.MasterPageNumber);
                    Assert.Equal(0, control.PreviewScroller.VerticalOffset);
                }
                finally
                {
                    window.Close();
                    Pump(200);
                }
            });
        }

        [Fact]
        public void TestMouseWheelInteraction_WhenFitInViewport()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var window = new Window
                {
                    Width = 1400,
                    Height = 1200,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                };
                var control = new PrintPreviewControl();
                window.Content = control;
                try
                {
                    control.Initialize(MakeSource(2), new PrintService(), "1-1",
                        new PrintSettings { IncludeProductionCopy = true, Copies = 1 });
                    window.Show();
                    Pump(500);

                    // Set zoom to 50% so whole sheet easily fits in viewport (ScrollableHeight == 0)
                    var applyZoomMethod = typeof(PrintPreviewControl).GetMethod("ApplyZoom", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    applyZoomMethod.Invoke(control, new object[] { 0.5 });
                    Pump(100);

                    Assert.Equal(1, control.DocumentReader.MasterPageNumber);
                    Assert.True(control.PreviewScroller.ScrollableHeight <= 1);

                    // Roll wheel down: should turn to Page 2 at top (offset 0)
                    System.Threading.Thread.Sleep(200);
                    control.PreviewScroller.RaiseEvent(new MouseWheelEventArgs(InputManager.Current.PrimaryMouseDevice, Environment.TickCount, -120)
                    {
                        RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    });
                    Pump(200);

                    Assert.Equal(2, control.DocumentReader.MasterPageNumber);
                    Assert.Equal(0, control.PreviewScroller.VerticalOffset);

                    // Roll wheel up: should turn back to Page 1 at top (offset 0)
                    System.Threading.Thread.Sleep(200);
                    control.PreviewScroller.RaiseEvent(new MouseWheelEventArgs(InputManager.Current.PrimaryMouseDevice, Environment.TickCount, 120)
                    {
                        RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    });
                    Pump(200);

                    Assert.Equal(1, control.DocumentReader.MasterPageNumber);
                    Assert.Equal(0, control.PreviewScroller.VerticalOffset);
                }
                finally
                {
                    window.Close();
                    Pump(200);
                }
            });
        }

        [Fact]
        public void DocumentReader_HasNoBuiltInToolbarOrButtons()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var window = new Window
                {
                    Width = 800,
                    Height = 600,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                };
                var control = new PrintPreviewControl();
                window.Content = control;
                try
                {
                    control.Initialize(MakeSource(2), new PrintService(), "1-1",
                        new PrintSettings { IncludeProductionCopy = true, Copies = 1 });
                    window.Show();
                    Pump(500);

                    // No ToolBarTray or ToolBar
                    Assert.Null(FindVisualChild<ToolBarTray>(control.DocumentReader));
                    Assert.Null(FindVisualChild<ToolBar>(control.DocumentReader));

                    // No built-in zoom slider
                    Assert.Null(FindVisualChild<Slider>(control.DocumentReader));

                    // No built-in RepeatButtons (zoom +/- or next/prev page buttons inside viewer)
                    Assert.Null(FindVisualChild<System.Windows.Controls.Primitives.RepeatButton>(control.DocumentReader));

                    // The actual document page view is present and visible
                    var pageView = FindVisualChild<System.Windows.Controls.Primitives.DocumentPageView>(control.DocumentReader);
                    Assert.NotNull(pageView);
                    Assert.Equal(Visibility.Visible, pageView!.Visibility);
                }
                finally
                {
                    window.Close();
                    Pump(200);
                }
            });
        }

        [Fact]
        public void PrintPreviewSource_SetsPageOrderStandard_OnPrintTicket()
        {
            string source = ReadPreviewSource();
            Assert.Contains("ticket.PageOrder = PageOrder.Standard;", source);
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var deep = FindVisualChild<T>(child);
                if (deep != null) return deep;
            }
            return null;
        }

        private static string ReadPreviewSource()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "MosquitoNetCalculator");
                if (File.Exists(Path.Combine(candidate, "App.xaml")))
                    return File.ReadAllText(Path.Combine(candidate, "Controls", "PrintPreviewControl.xaml.cs"));
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate MosquitoNetCalculator source project.");
        }
    }
}
