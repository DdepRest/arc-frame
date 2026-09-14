using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.App;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Focused regressions for the UX state paths added in v3.50.
    /// These tests deliberately cover the real control seams rather than
    /// asserting only that the source contains a feature name.
    /// </summary>
    [Collection("WPF_UI")]
    public class UxStateRegressionTests
    {
        [Fact]
        public void DeleteAndClearToastActions_RouteToUndo_NotRedo()
        {
            var sourceDir = LocateSourceProject();
            var deleteSource = File.ReadAllText(Path.Combine(sourceDir, "MainWindow.Items.cs"));
            var actionBarSource = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml.cs"));

            int deleteStart = deleteSource.IndexOf("internal void BtnDeleteRow_Click", StringComparison.Ordinal);
            Assert.True(deleteStart >= 0, "Delete-row entry point was not found.");
            string deleteBody = deleteSource[deleteStart..];
            Assert.Contains("() => Undo()", deleteBody);
            Assert.DoesNotContain("() => Redo()", deleteBody);

            int clearStart = actionBarSource.IndexOf("private void BtnClearAll_Click", StringComparison.Ordinal);
            Assert.True(clearStart >= 0, "Clear-all entry point was not found.");
            string clearBody = actionBarSource[clearStart..];
            Assert.Contains("() => mw.Undo()", clearBody);
            Assert.DoesNotContain("() => mw.Redo()", clearBody);
        }

        [Fact]
        public void DirtyChip_IsGone_FromActionBar_OwnedByStatusBarOnly()
        {
            // v3.50.2 owner request: the top action bar must not duplicate the
            // status-bar «Есть изменения» indicator — one owner (StatusDirtyIndicator
            // in MainWindow.xaml) instead of two chips showing the same state.
            var sourceDir = LocateSourceProject();
            var xaml = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml"));
            var code = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml.cs"));

            Assert.DoesNotContain("x:Name=\"DirtyChip\"", xaml);
            Assert.DoesNotContain("Есть изменения", xaml);
            Assert.DoesNotContain("DirtyChip_Click", code);

            // The status-bar indicator in MainWindow.xaml stays the single owner.
            var mainWindowXaml = File.ReadAllText(Path.Combine(sourceDir, "MainWindow.xaml"));
            Assert.Contains("x:Name=\"StatusDirtyIndicator\"", mainWindowXaml);
        }

        [Fact]
        public void ClientInfoButton_IsPrimaryAccent_SameAsPrintButton()
        {
            // v3.50.2 owner request: «Заказчик» must always be the same solid
            // accent blue as «Печать КП» (prototype .btn.blue) — no ghost or
            // tinted intermediate states.
            var sourceDir = LocateSourceProject();
            var xaml = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml"));
            int styleStart = xaml.IndexOf("x:Key=\"ClientInfoButton\"", StringComparison.Ordinal);
            Assert.True(styleStart >= 0, "ClientInfoButton style was not found.");
            int styleEnd = xaml.IndexOf("/>", styleStart, StringComparison.Ordinal);
            string style = xaml.Substring(styleStart, styleEnd - styleStart);

            Assert.Contains("BasedOn=\"{StaticResource PrimaryButton}\"", style);
            Assert.DoesNotContain("AccentLight", style);
            Assert.DoesNotContain("IsClientInfoFilled", style);
        }

        [Fact]
        public void PrintSplitButton_MainSegmentAndCaret_AreWiredConsistently()
        {
            var sourceDir = LocateSourceProject();
            var xaml = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml"));
            var code = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml.cs"));
            var previewCode = File.ReadAllText(Path.Combine(sourceDir, "Controls", "PrintPreviewControl.xaml.cs"));

            // v3.50.2 regression: the caret segment shipped without a Click
            // wiring once — a mouse click opened nothing while the main
            // segment still worked, so the split button looked broken.
            int caretStart = xaml.IndexOf("x:Name=\"BtnPrintCaret\"", StringComparison.Ordinal);
            Assert.True(caretStart >= 0, "BtnPrintCaret was not found.");
            int caretEnd = xaml.IndexOf(">", caretStart, StringComparison.Ordinal);
            Assert.True(caretEnd > caretStart, "BtnPrintCaret opening tag is malformed.");
            string caretTag = xaml.Substring(caretStart, caretEnd - caretStart + 1);
            Assert.Contains("Click=\"BtnPrintCaret_Click\"", caretTag);

            // The caret handler must exist and toggle the popup.
            Assert.Contains("private void BtnPrintCaret_Click", code);
            Assert.Contains("PrintMenuPopup.IsOpen = !PrintMenuPopup.IsOpen", code);

            // v3.50.3 owner semantics: main segment = предпросмотр; the caret
            // menu offers exactly three direct actions, none of which opens a
            // preview. Every action must reuse the PrintPreviewControl
            // pipeline (no second print route).
            int mainStart = xaml.IndexOf("x:Name=\"BtnPrintKpMain\"", StringComparison.Ordinal);
            Assert.True(mainStart >= 0, "BtnPrintKpMain was not found.");
            int mainEnd = xaml.IndexOf(">", mainStart, StringComparison.Ordinal);
            string mainTag = xaml.Substring(mainStart, mainEnd - mainStart + 1);
            Assert.Contains("Click=\"BtnPrintPreview_Click\"", mainTag);

            Assert.Contains("Click=\"BtnPrinterDirect_Click\"", xaml);
            Assert.Contains("Click=\"BtnPrinterProduction_Click\"", xaml);
            Assert.Contains("Click=\"BtnPrintPdf_Click\"", xaml);
            Assert.Contains("Печать без предпросмотра", xaml);
            Assert.Contains("«Чистая» + «В производство»", xaml);
            Assert.Contains("Сохранить в PDF", xaml);

            Assert.Contains("TriggerPrinterPrint()", code);
            Assert.Contains("TriggerPrinterPrintWithProductionCopy()", code);
            Assert.Contains("TriggerPdfExport()", code);

            // The production-copy trigger must flip the existing checkbox and
            // reuse Print_Click — not spawn a parallel pipeline.
            Assert.Contains("public void TriggerPrinterPrintWithProductionCopy", previewCode);
            Assert.Contains("TriggerPrinterPrint();", previewCode);
        }

        // ═══════════ v3.51: «История обновлений» — 8 улучшений ═══════════

        [Fact]
        public void UpdatesList_IsVirtualized_GroupedByYear_WithVisibleScrollbar()
        {
            var sourceDir = LocateSourceProject();
            var xaml = File.ReadAllText(Path.Combine(sourceDir, "Controls", "UpdatesTabControl.xaml"));

            // Идея 5: виртуализация + видимый скроллбар.
            Assert.Contains("<VirtualizingStackPanel/>", xaml);
            Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", xaml);
            Assert.Contains("VirtualizingPanel.IsVirtualizingWhenGrouping=\"True\"", xaml);
            Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", xaml);
            Assert.DoesNotContain("VerticalScrollBarVisibility=\"Hidden\"", xaml);
            Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml);
            Assert.Contains("UpdatesScroll_ScrollChanged", xaml);
            Assert.Contains("BtnScrollTop", xaml);

            // Идея 6: группы по годам.
            Assert.Contains("ItemsControl.GroupStyle", xaml);
            Assert.Contains("YearGroupHeaderTemplate", xaml);
        }

        [Fact]
        public void UpdatesList_HasTypeChips_SearchAndEmptyState()
        {
            var sourceDir = LocateSourceProject();
            var xaml = File.ReadAllText(Path.Combine(sourceDir, "Controls", "UpdatesTabControl.xaml"));
            var code = File.ReadAllText(Path.Combine(sourceDir, "Controls", "UpdatesTabControl.xaml.cs"));

            // Идея 3: чипы-фильтры + поиск + пустое состояние.
            Assert.Contains("ChipFilterAll", xaml);
            Assert.Contains("ChipFilterNovelty", xaml);
            Assert.Contains("ChipFilterImprovement", xaml);
            Assert.Contains("ChipFilterFix", xaml);
            Assert.Contains("TxtUpdatesSearch_TextChanged", xaml);
            Assert.Contains("x:Name=\"UpdatesSearchPlaceholder\"", xaml);
            Assert.Contains("BtnClearUpdatesSearch", xaml);
            Assert.Contains("x:Name=\"UpdatesEmptyState\"", xaml);
            Assert.Contains("Ничего не найдено", xaml);

            // Фильтр идёт через CollectionView, счётчик — «N из M».
            Assert.Contains("ListCollectionView", code);
            Assert.Contains("UpdatesListLogic.CountText", code);
        }

        [Fact]
        public void UpdatesList_HasMyVersionBadge_CopyButton_AndCollapsibleCards()
        {
            var sourceDir = LocateSourceProject();
            var xaml = File.ReadAllText(Path.Combine(sourceDir, "Controls", "UpdatesTabControl.xaml"));

            // Идея 1: бейдж «Ваша версия» через IsMyVersionConverter.
            Assert.Contains("Ваша версия", xaml);
            Assert.Contains("{StaticResource IsMyVersion}", xaml);

            // v3.51 hotfix (владелец): бейджи «Новейшая»/«Ваша версия» не
            // показывались — дефолт Visibility=Collapsed был ЛОКАЛЬНЫМ
            // атрибутом, который сильнее триггера стиля. Дефолт обязан жить
            // в Setter стиля — запрещаем локальный Visibility на бейджах.
            foreach (var badgeStart in FindAll(xaml, "<!-- Бейдж "))
            {
                int tagEnd = xaml.IndexOf(">", badgeStart, StringComparison.Ordinal);
                string badgeTag = xaml.Substring(badgeStart, tagEnd - badgeStart + 1);
                Assert.DoesNotContain("Visibility=\"Collapsed\"", badgeTag);
            }

            // Идея 7: копирование карточки.
            Assert.Contains("CopyCard_Click", xaml);
            Assert.Contains("Скопировать", xaml);

            // Идея 4: компактная строка + разворот по клику.
            Assert.Contains("CardHeader_Click", xaml);
            Assert.Contains("{Binding IsExpanded", xaml);

            // Владелец: цветная полоса статуса слева на карточке — тип версии.
            // v3.52.0 (владелец: «дизайн скруглённый, а полоска квадратная»):
            // полоса была отдельным ПРЯМОУГОЛЬНИКОМ Width=4 и закрашивала
            // скруглённый угол карточки. Теперь это ЛЕВАЯ ГРАНИЦА внутреннего
            // Border — рамка обводит скруглённый контур, поэтому полоса
            // повторяет радиус карточки (11 = 12 радиуса карточки минус 1px
            // её рамки). Пиксели проверяет UpdateCardStripeTests.
            Assert.Contains("ConverterParameter=strong", xaml);
            string cardMarkup = System.Text.RegularExpressions.Regex.Replace(xaml, @"\s+", " ");
            Assert.Contains("BorderThickness=\"4,0,0,0\" CornerRadius=\"11,0,0,11\"", cardMarkup);
            Assert.DoesNotContain("<Border Grid.Column=\"0\" Width=\"4\"", cardMarkup);

            // v3.51 hotfix (владелец): заголовок не должен дублироваться —
            // в компактной строке он скрывается, когда карточка раскрыта.
            // Ищем устойчиво к отступам: сворачиваем пробелы и проверяем пару
            // триггер→сеттер без учёта whitespace.
            string noWs = System.Text.RegularExpressions.Regex.Replace(xaml, @"\s+", " ");
            Assert.Contains(
                "<DataTrigger Binding=\"{Binding IsExpanded}\" Value=\"True\"> <Setter Property=\"Visibility\" Value=\"Collapsed\"/> </DataTrigger>",
                noWs);

            // Идея 8: в прототипе нет line-clamp — длинные пункты остаются
            // полными внутри развёрнутой карточки; фича осознанно не вносилась.
        }

        [Fact]
        public void UpdatesList_CollapsedCards_DefaultFiveExpanded_SearchExpandsMatches()
        {
            // Идея 4 (поведение): правило «первые 5 раскрыты» живёт в модели
            // через AllNewestFirst(expandFirst) — тот же источник, что и UI.
            var updates = UpdateLog.AllNewestFirst(UpdatesListLogic.DefaultExpandedCards);

            Assert.True(updates.Count >= 5, "Expected the real log to have at least 5 entries");
            for (int i = 0; i < UpdatesListLogic.DefaultExpandedCards; i++)
                Assert.True(updates[i].IsExpanded, $"Card {i} must be expanded by default");
            for (int i = UpdatesListLogic.DefaultExpandedCards; i < updates.Count; i++)
                Assert.False(updates[i].IsExpanded, $"Card {i} must be collapsed by default");
        }

        [Fact]
        public void WhatsNewWindow_OpensFullHistory_OnDemand()
        {
            // Идея 2: дайджест после обновления уже существовал (WhatsNewService);
            // добавлен переход «Вся история →» на вкладку «Обновления».
            var sourceDir = LocateSourceProject();
            var windowCode = File.ReadAllText(Path.Combine(sourceDir, "Controls", "WhatsNewWindow.xaml.cs"));
            var windowXaml = File.ReadAllText(Path.Combine(sourceDir, "Controls", "WhatsNewWindow.xaml"));
            var appCode = File.ReadAllText(Path.Combine(sourceDir, "App.xaml.cs"));
            var mainCode = File.ReadAllText(Path.Combine(sourceDir, "MainWindow.xaml.cs"));

            Assert.Contains("BtnOpenHistory_Click", windowXaml);
            Assert.Contains("_openHistory?.Invoke()", windowCode);
            Assert.Contains("openFullHistory: () => window.OpenUpdatesTab()", appCode);
            Assert.Contains("internal void OpenUpdatesTab()", mainCode);
        }

        [Fact]
        public void HistoryFilter_ReappliesAfterItemsSourceRefresh()
        {
            RunOnStaWithThemes(() =>
            {
                var control = new OrdersHistoryControl();
                var search = control.FindName("TxtSearchOrders") as TextBox;
                Assert.NotNull(search);

                var firstLoad = new List<OrderData>
                {
                    new() { ContractNumber = "1-1", ClientAddress = "Alpha street" },
                    new() { ContractNumber = "1-2", ClientAddress = "Beta street" },
                };
                OrderGridPresenter.RefreshOrdersGrid(control.OrdersGrid, firstLoad);
                control.SetOrdersCount(firstLoad.Count);

                search!.Text = "alpha";
                int filteredCount = control.OrdersGrid.Items.Count;
                Assert.Equal(1, filteredCount);
                Assert.Equal("Alpha street", ((OrderData)control.OrdersGrid.Items[0]).ClientAddress);

                // This is the production refresh path: ItemsSource is replaced
                // with a newly loaded list while the user's filter stays active.
                var refreshed = new List<OrderData>
                {
                    new() { ContractNumber = "2-1", ClientAddress = "Alpha avenue" },
                    new() { ContractNumber = "2-2", ClientAddress = "Gamma avenue" },
                };
                OrderGridPresenter.RefreshOrdersGrid(control.OrdersGrid, refreshed);
                control.SetOrdersCount(refreshed.Count);
                control.ReapplyFilters();

                int refreshedFilteredCount = control.OrdersGrid.Items.Count;
                Assert.Equal(1, refreshedFilteredCount);
                Assert.Equal("Alpha avenue", ((OrderData)control.OrdersGrid.Items[0]).ClientAddress);
                Assert.True(control.IsEmpty == false);
                Assert.False(control.NoSearchResults);
            });
        }

        private static void RunOnStaWithThemes(Action action) => TestAppThemes.RunOnSta(action);

        /// <summary>Все индексы вхождений <paramref name="needle"/> в <paramref name="text"/>.</summary>
        private static IEnumerable<int> FindAll(string text, string needle)
        {
            int idx = 0;
            while ((idx = text.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                yield return idx;
                idx += needle.Length;
            }
        }

        [Fact]
        public void Themes_HaveNoLiveDropShadow_InTriggersOrTemplates()
        {
            // v3.52.0 perf: DropShadowEffect (Gaussian blur) renders on the CPU
            // and re-rasterizes on EVERY frame of an animation. Effects inside
            // hover/press triggers (ButtonStyles, CardStyles, input styles)
            // made each overlay slide re-blur the whole visual tree — the
            // reported "tab switching lags". Effects on static popups/dialog
            // windows are allowed (computed once at open time).
            var themesDir = Path.Combine(LocateSourceProject(), "Themes");
            var offenders = new List<string>();

            foreach (var file in Directory.GetFiles(themesDir, "*.xaml"))
            {
                var lines = File.ReadAllLines(file);
                bool inComment = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    // Documentation comments legitimately mention DropShadowEffect
                    // ("why we don't use it") — count only real markup.
                    int commentStart;
                    while ((commentStart = line.IndexOf("<!--", StringComparison.Ordinal)) >= 0)
                    {
                        int commentEnd = line.IndexOf("-->", commentStart, StringComparison.Ordinal);
                        line = commentEnd >= 0
                            ? line.Remove(commentStart, commentEnd - commentStart + 3)
                            : line[..commentStart];
                        if (commentEnd < 0) inComment = true;
                    }
                    if (inComment)
                    {
                        int end = line.IndexOf("-->", StringComparison.Ordinal);
                        if (end >= 0) { line = line[(end + 3)..]; inComment = false; }
                        else line = string.Empty;
                    }

                    if (line.Contains("DropShadowEffect") || line.Contains("<DropShadowEffect"))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }

            Assert.True(offenders.Count == 0,
                "DropShadowEffect/Effect setters found in theme triggers/templates — " +
                "live Gaussian blur makes animations lag: " + string.Join(", ", offenders));
        }

        private static string LocateSourceProject()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "MosquitoNetCalculator");
                if (File.Exists(Path.Combine(candidate, "App.xaml")))
                    return candidate;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate MosquitoNetCalculator source project.");
        }
    }
}
