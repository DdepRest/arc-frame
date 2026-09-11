using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// PLAYTEST (throwaway): панель открывается «первым пользователем» — реальный
    /// AdminPanelControl на STA-потоке с реальными темами App.xaml, данными из
    /// чистых конструкторов моделей. Находит дефекты запуска/интеракции, которые
    /// не ловит ни сборка, ни чистые тесты.
    /// <para>WPF_UI — сериализованная коллекция (как у AppLifecycleTests): каждый
    /// тест создаёт свой Application на своём STA-потоке и чинит статик WPF в
    /// конце, чтобы соседние STA-тесты могли сделать то же.</para>
    /// </summary>
    [Collection("WPF_UI")]
    public class AdminPanelPlaytestTests
    {
        private static string FindSourceDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "MosquitoNetCalculator");
                if (File.Exists(Path.Combine(candidate, "App.xaml"))) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("source not found");
        }

        // Один тест — один STA-поток и один Application (по образцу
        // AppLifecycleTests): статик WPF «не более одного Application в
        // AppDomain» чистится в конце, чтобы не ломать соседние STA-тесты.
        private static void RunOnSta(Action action)
        {
            Exception? caught = null, cleanupError = null;
            var gate = new ManualResetEventSlim(false);

            var t = new Thread(() =>
            {
                try
                {
                    // Чужой Application в AppDomain (параллельный STA-тест)
                    // помешает создать наш — чистим статик перед стартом.
                    if (Application.Current != null)
                        MosquitoNetCalculator.Tests.App.AppLifecycleTests.ClearWpfApplicationStatic();

                    EnsureAppThemes();
                    action();
                }
                catch (Exception ex) { caught = ex; }
                finally
                {
                    try { MosquitoNetCalculator.Tests.App.AppLifecycleTests.ClearWpfApplicationStatic(); }
                    catch (Exception ex) { cleanupError = ex; }
                    gate.Set();
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();

            Assert.True(gate.Wait(TimeSpan.FromSeconds(90)), "STA playtest thread did not finish in time");
            t.Join();

            if (caught != null) throw caught;
            if (cleanupError != null)
                throw new InvalidOperationException("playtest WPF cleanup failed", cleanupError);
        }

        /// <summary>
        /// Ровно тот же набор словарей в ТОМ ЖЕ порядке, что App.xaml:
        /// StaticResource между словарями требует порядок загрузки
        /// (FluentFocusVisual до словарей, которые его используют).
        /// </summary>
        private static void EnsureAppThemes()
        {
            if (Application.Current != null && Application.Current.Resources.MergedDictionaries.Count > 0)
                return;

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var src = FindSourceDir();
            string[] order =
            {
                "Brushes.xaml", "FocusVisualStyles.xaml", "CardStyles.xaml",
                "FontStyles.xaml", "TabStyles.xaml", "ButtonStyles.xaml",
                "InputStyles.xaml", "DataGridStyles.xaml", "InputStyles.RadioButton.xaml",
                "ScrollViewerStyles.xaml", "ContextMenuStyles.xaml", "MiscStyles.xaml",
            };
            foreach (var name in order)
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri(Path.Combine(src, "Themes", name), UriKind.Absolute) });
            }

            // Конвертеры из App.xaml (StaticResource панели ссылается на них —
            // в продукте их регистрирует App.xaml).
            app.Resources["OfficeStatusBadgeBg"] = new MosquitoNetCalculator.Converters.OfficeStatusToBadgeBackgroundConverter();
            app.Resources["OfficeStatusBadgeFg"] = new MosquitoNetCalculator.Converters.OfficeStatusToBadgeForegroundConverter();
            app.Resources["OfficeStatusStripeBrush"] = new MosquitoNetCalculator.Converters.OfficeStatusToStripeBrushConverter();
            app.Resources["UnbindModeToVisibility"] = new MosquitoNetCalculator.Converters.UnbindModeToVisibilityConverter();
            app.Resources["BoolToVis"] = new System.Windows.Controls.BooleanToVisibilityConverter();
        }

        /// <summary>
        /// Открывает панель, наполняет Rows/StatsRows (как это делает RefreshAsync),
        /// применяет пустые состояния. Возвращает панель для интеракций.
        /// </summary>
        private static AdminPanelControl OpenPanelWithData(
            out List<OfficeStatusRow> rows,
            OfficeStatus? overrideStatus = null)
        {
            EnsureAppThemes();
            var panel = new AdminPanelControl();
            // Без Measure/Arrange у контрола нет визуального дерева (шаблон не
            // применён) — эмулируем размещение в окне, как при реальном показе.
            panel.Measure(new Size(900, 800));
            panel.Arrange(new Rect(0, 0, 900, 800));
            panel.UpdateLayout();

            var now = DateTimeOffset.Now;
            OfficeDeviceRow Dev(string name, string ver, OfficeStatus st, DateTimeOffset at) => new()
            {
                DeviceId = "id-" + name,
                DeviceName = name,
                Version = ver,
                LastReportAt = at,
                Status = st,
            };

            var status = overrideStatus;
            var list = new List<OfficeStatusRow>
            {
                new() { Prefix = "2", LocationName = LocationOptions.All.First(o => o.Prefix == "2").LocationName,
                    Status = status ?? OfficeStatus.Outdated, IsCurrentOffice = false, LastReportAt = now.AddMinutes(-40),
                    DeviceCount = 3, Devices = new[] { Dev("DESKTOP-BTLHI45", "3.49.0", OfficeStatus.UpToDate, now.AddMinutes(-40)),
                        Dev("DESKTOP-KT3K5UK", "3.49.0", OfficeStatus.UpToDate, now.AddMinutes(-41)),
                        Dev("DESKTOP-OOJHNVB", "3.48.7", OfficeStatus.Outdated, now.AddMinutes(-42)) } },
                new() { Prefix = "1", LocationName = LocationOptions.All.First(o => o.Prefix == "1").LocationName,
                    Status = status ?? OfficeStatus.Outdated, IsCurrentOffice = true, LastReportAt = now.AddMinutes(-90),
                    DeviceCount = 1, Devices = new[] { Dev("DESKTOP-OOJHNVB", "3.48.7", OfficeStatus.Outdated, now.AddMinutes(-90)) } },
                new() { Prefix = "5", LocationName = LocationOptions.All.First(o => o.Prefix == "5").LocationName,
                    Status = OfficeStatus.UpToDate, IsCurrentOffice = false, LastReportAt = now.AddMinutes(-10),
                    DeviceCount = 1, Devices = new[] { Dev("DESKTOP-K4RDFUN", "3.49.0", OfficeStatus.UpToDate, now.AddMinutes(-10)) } },
                new() { Prefix = "3", LocationName = LocationOptions.All.First(o => o.Prefix == "3").LocationName,
                    Status = OfficeStatus.NoData, IsCurrentOffice = false, DeviceCount = 1,
                    Devices = new[] { Dev("DESKTOP-GHOST", "3.44.0", OfficeStatus.NoData, now.AddDays(-8)) } },
                new() { Prefix = "4", LocationName = LocationOptions.All.First(o => o.Prefix == "4").LocationName,
                    Status = OfficeStatus.NoData, IsCurrentOffice = false, DeviceCount = 0, Devices = Array.Empty<OfficeDeviceRow>() },
            };

            panel.ReplaceRows(list);

            // Контейнеры ItemsControl материализуются асинхронно (диспетчер):
            // прокачиваем очередь до простоя и перестраиваем layout.
            FlushUi(panel);

            rows = list;
            return panel;
        }

        /// <summary>
        /// Прогоняет очередь диспетчера до простоя и применяет layout —
        /// ItemsControl генерирует контейнеры асинхронно, читать дерево можно
        /// только после этого (в реальном приложении это происходит между кадрами).
        /// </summary>
        private static void FlushUi(AdminPanelControl panel)
        {
            panel.Measure(new Size(900, 800));
            panel.Arrange(new Rect(0, 0, 900, 800));
            for (int i = 0; i < 2; i++)
            {
                // ContextIdle ниже DataBind/Render/Loaded/Background: ждём ВСЕ
                // отложенные операции — честный барьер «кадр дорисован».
                panel.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
                panel.UpdateLayout();
            }
        }

        private static T? Find<T>(DependencyObject root, string name) where T : class
        {
            if (root is FrameworkElement fe && fe.Name == name) return fe as T;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var found = Find<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }
        private static int ItemCount(FrameworkElement container)
        {
            int count = 0;
            int n = VisualTreeHelper.GetChildrenCount(container);
            for (int i = 0; i < n; i++)
            {
                if (VisualTreeHelper.GetChild(container, i) is ContentPresenter cp &&
                    cp.Content is OfficeStatusRow) count++;
                count += ItemCount((FrameworkElement)(VisualTreeHelper.GetChild(container, i)));
            }
            return count;
        }

        [Fact]
        public void Playtest_PanelOpens_GroupsAndFiltersRespondToClicks()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out var data);

                // Карточки реально отрисовались (панель открылась, а не упала).
                Assert.Equal(5, ItemCount(panel.OfficesList));

                // 1) Фильтр «Устаревшие» кликом по чипу; обе строки делят
                // один статус → ОДНА группа «УСТАРЕВШИЕ».
                ClickChip(panel, "ChipFilterOutdated");
                Assert.Equal(2, ItemCount(panel.OfficesList));
                var headers = VisibleGroupHeaders(panel.OfficesList);
                var single = Assert.Single(headers);
                Assert.Contains("УСТАРЕВШИЕ", single);

                // 2) Поиск «жукова» при активном фильтре → пусто; «Ничего не найдено».
                TypeIntoSearch(panel, "жукова");
                Assert.Equal(0, ItemCount(panel.OfficesList));
                var empty = Find<StackPanel>(panel, "UpdatesEmpty");
                var title = Find<TextBlock>(panel, "UpdatesEmptyTitle");
                Assert.Equal(Visibility.Visible, empty!.Visibility);
                Assert.Contains("Ничего не найдено", title!.Text);

                // 3) Сброс: «Все» + очистка поиска → снова 5.
                ClickChip(panel, "ChipFilterAll");
                ClearSearch(panel);
                Assert.Equal(5, ItemCount(panel.OfficesList));

                // 4) Поиск по префиксу «76» → офис 2.
                TypeIntoSearch(panel, "76");
                Assert.Equal(1, ItemCount(panel.OfficesList));
            });
        }

        [Fact]
        public void Playtest_RapidFilterChipSwitching_DoesNotBreakList()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out _);

                for (int i = 0; i < 25; i++)
                {
                    ClickChip(panel, i % 2 == 0 ? "ChipFilterOutdated" : "ChipFilterAll");
                    TypeIntoSearch(panel, i % 3 == 0 ? "рут" : "");
                }
                ClearSearch(panel);
                ClickChip(panel, "ChipFilterAll");

                Assert.Equal(5, ItemCount(panel.OfficesList));
                Assert.True(Find<Button>(panel, "BtnRemindAll")!.IsEnabled);
            });
        }

        [Fact]
        public void Playtest_OutdatedCardClick_CopiesReminderText()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out _);

                // Клик по устаревшей карточке офиса (первая строка в Rows — Outdated).
                var firstCard = FindOfficeCardByPrefix(panel, "2");
                Assert.NotNull(firstCard);
                firstCard!.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Control.PreviewMouseLeftButtonUpEvent });

                var text = Clipboard.GetText();
                Assert.Contains("Рудакова 76", text);
                Assert.Contains("3.48.7", text);
            });
        }

        [Fact]
        public void Playtest_RemindAllButton_CopiesCombinedMessage_DisabledWithoutOutdated()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out _);

                var btn = Find<Button>(panel, "BtnRemindAll");
                Assert.True(btn!.IsEnabled);
                btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                var text = Clipboard.GetText();
                Assert.Contains("Рудакова 76", text);
                Assert.Contains("Красношапки 44", text);
                // В сообщении — версии УСТАРЕВШИХ устройств (3.48.7), не актуальных.
                Assert.Contains("3.48.7", text);
                Assert.DoesNotContain("3.49.0", text);

                // Все офисы актуальны → кнопка дизейблится (UpdateQuickActionsState).
                panel.Rows.Clear();
                panel.Rows.Add(new OfficeStatusRow { Prefix = "1", LocationName = "x", Status = OfficeStatus.UpToDate, DeviceCount = 1 });
                panel.UpdateQuickActionsState();
                Assert.False(btn.IsEnabled);
            });
        }

        [Fact]
        public void Playtest_UnbindMode_CheckboxVisualsAlwaysMatchPendingSelection()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out _);
                string Banner() => Find<TextBlock>(panel, "TxtUnbindBanner")!.Text;

                // Входим в режим отвязки: баннер виден.
                var unbindBtn = Find<Button>(panel, "BtnUnbindMode");
                Assert.NotNull(unbindBtn);
                unbindBtn!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                FlushUi(panel);
                Assert.Equal(Visibility.Visible,
                    Find<Border>(panel, "UnbindModeBanner")!.Visibility);
                Assert.Contains("Отметьте устройства", Banner());

                // Отмечаем чекбокс первого устройства.
                var check = Find<System.Windows.Controls.CheckBox>(panel, "DeviceUnbindCheck");
                Assert.NotNull(check);
                check!.IsChecked = true;
                Assert.Contains("Отмечено устройств: 1", Banner());

                // ОСТОРОЖНЫЙ пользователь: смена фильтра при отмеченном чипе.
                // Отметка живёт на модели → видимое всегда совпадает с реальным
                // выбором; баннер остаётся правдивым.
                ClickChip(panel, "ChipFilterUpToDate");
                Assert.Contains("Отмечено устройств: 1", Banner());

                // Возврат фильтра: КАЖДЫЙ видимый чекбокс показывает отметку
                // СВОЕГО устройства — инвариант против ghost selection при
                // переиспользовании контейнеров виртуализацией.
                ClickChip(panel, "ChipFilterOutdated");
                var checkedCount = AllChecks(panel).Count(b => b.IsChecked == true);
                Assert.Equal(1, checkedCount);
                var selectedDevice = AllChecks(panel).Single(b => b.IsChecked == true).DataContext
                    as MosquitoNetCalculator.Models.OfficeDeviceRow;
                Assert.Equal("id-DESKTOP-BTLHI45", selectedDevice!.DeviceId);
            });
        }

        [Fact]
        public void Playtest_EmptyData_ShowsNoDataState_QuickActionsDisabled()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out _);

                // Рефреш принёс ноль отчётов (все устройства отвязаны/ещё не reporting):
                // те же шаги, что делает реальный RefreshAsync после чтения gist.
                panel.ReplaceRows(Array.Empty<OfficeStatusRow>());
                panel.UpdateSummaryProgress(0, 0);
                panel.UpdateQuickActionsState();
                panel.UpdateEmptyStates();
                FlushUi(panel);

                Assert.Equal(Visibility.Visible, Find<StackPanel>(panel, "UpdatesEmpty")!.Visibility);
                Assert.Equal(Visibility.Collapsed, panel.OfficesList.Visibility);
                Assert.False(Find<Button>(panel, "BtnRemindAll")!.IsEnabled);
            });
        }        [Fact]
        public void Playtest_JustUnboundMarker_ShowsAfterUnbind_HidesWhenDeviceReportsBack()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out var originalRows);
                var marker = Find<TextBlock>(panel, "TxtJustUnbound");
                Assert.NotNull(marker);

                // Маркер скрыт, пока ничего не отвязывали.
                Assert.Equal(Visibility.Collapsed, marker!.Visibility);

                // Админ отвязал устройство офиса 2 (id из OpenPanelWithData).
                panel.RecordJustUnbound(new[] { "id-DESKTOP-BTLHI45" });
                FlushUi(panel);

                Assert.Equal(Visibility.Visible, marker.Visibility);
                Assert.Contains("DESKTOP-BTLHI45", marker.Text);
                Assert.Contains("вернутся с отчётом", marker.Text);

                // РЕФРЕШ ПОСЛЕ ОТВЯЗКИ (как в продукте): файл удалён → устройства
                // в отчётах НЕТ. Маркер живёт — это и есть «отвязали недавно».
                ReplaceAllRows(panel, RowsWithout(panel, "id-DESKTOP-BTLHI45"));
                Assert.Equal(Visibility.Visible, marker.Visibility);

                // Устройство вернулось с отчётом → маркер гаснет.
                ReplaceAllRows(panel, originalRows);
                Assert.Equal(Visibility.Collapsed, marker.Visibility);
            });
        }

        /// <summary>Строки панели без указанного устройства (модель «файл удалён»).</summary>
        private static List<OfficeStatusRow> RowsWithout(AdminPanelControl panel, string deviceId) =>
            panel.Rows.Select(r => new OfficeStatusRow
            {
                Prefix = r.Prefix,
                LocationName = r.LocationName,
                Status = r.Status,
                IsCurrentOffice = r.IsCurrentOffice,
                LastReportAt = r.LastReportAt,
                DeviceCount = r.Devices.Count(d => d.DeviceId != deviceId),
                Devices = r.Devices.Where(d => d.DeviceId != deviceId).ToList(),
            }).ToList();

        /// <summary>Прогнать строки через реальный ReplaceRows (как делает RefreshAsync).</summary>
        private static void ReplaceAllRows(AdminPanelControl panel, List<OfficeStatusRow> rows)
        {
            panel.ReplaceRows(rows);
            FlushUi(panel);
        }

        [Fact]
        public void Playtest_LastSeenHint_ShowsOnStaleDeviceChip()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out _);
                FlushUi(panel);

                // В офисе 4 «призрак» без отчёта 8 дней — его чип несёт давность;
                // у свежих чипов (10–90 мин) подсказки быть не должно.
                var stale = AllChecks(panel)
                    .Where(b => b.DataContext is OfficeDeviceRow d && d.LastReportAt != null &&
                                DateTimeOffset.Now - d.LastReportAt.Value > TimeSpan.FromHours(12))
                    .ToList();
                Assert.NotEmpty(stale);

                foreach (var box in AllChecks(panel))
                {
                    var chip = (StackPanel)FindChipHint(box)!;
                    var hint = ((TextBlock)chip.Children[^1]).Text;
                    var device = (OfficeDeviceRow)box.DataContext;
                    var expected = device.LastReportAt != null &&
                                   DateTimeOffset.Now - device.LastReportAt.Value > TimeSpan.FromHours(12);
                    Assert.Equal(expected, hint.Length > 0);
                }
            });
        }

        /// <summary>Родительский StackPanel чипа для чекбокса (там последний ребёнок — подсказка).</summary>
        private static DependencyObject? FindChipHint(DependencyObject check)
        {
            var parent = VisualTreeHelper.GetParent(check);
            while (parent != null && parent is not StackPanel)
                parent = VisualTreeHelper.GetParent(parent);
            return parent;
        }

        [Fact]
        public void Playtest_CardClickHandler_DoesNotSwallowCheckboxClicks()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out _);

                // Входим в режим отвязки (кнопкой, как пользователь).
                var unbindBtn = Find<Button>(panel, "BtnUnbindMode");
                Assert.NotNull(unbindBtn);
                unbindBtn!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                FlushUi(panel);

                var check = Find<System.Windows.Controls.CheckBox>(panel, "DeviceUnbindCheck");
                Assert.NotNull(check);

                // КОНТРАКТ (дефект из продакшена «завис интерфейс»): туннелирующий
                // Preview клика по чекбоксу проходит через обработчик панели на
                // OfficesList РАНЬШЕ адресата. Если панель пометит его Handled,
                // WPF НЕ поднимет bubbling-пару MouseLeftButtonUp — чекбокс не
                // увидит MouseUp, НЕ ОТПУСТИТ захват мыши и интерфейс «зависнет».
                // RaiseEvent на чекбоксе строит честный маршрут «корень → чекбокс»
                // с OriginalSource = чекбокс — обработчик панели вызывается по пути.
                // КОНТРАКТ (дефект из продакшена «завис интерфейс»): обработчик
                // панели ВИСИТ НА OfficesList как PreviewMouseLeftButtonUp — он
                // туннелирует К КЛИКУ раньше адресата. RaiseEvent с этого чекбокса
                // маршрут от корня НЕ строит (панель не вызывается — проверено
                // диагностикой), поэтому вызываем обработчик как реальная мышь:
                // событие с OriginalSource = чекбокс, поднятое на OfficesList.
                MouseButtonEventArgs UpClick() => new(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Source = check };

                // 1) В РЕЖИМЕ ОТВЯЗКИ (продакшен-фриз): панель обязана выйти,
                // не помечая клик Handled — иначе чекбокс не получит MouseUp,
                // не отпустит захват мыши и интерфейс перестанет отвечать.
                var args1 = UpClick();
                panel.OfficesList.RaiseEvent(args1);
                Assert.False(args1.Handled,
                    "в режиме отвязки панель пометила Preview клика по чекбоксу как Handled — чекбокс «зависнет» с захватом мыши");

                // 2) ВНЕ режима: интерактивные дети карточки (чекбоксы, кнопки,
                // поля — и будущих версий) не наша цель — тоже пропуск.
                unbindBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                FlushUi(panel);
                var args2 = UpClick();
                panel.OfficesList.RaiseEvent(args2);
                Assert.False(args2.Handled,
                    "вне режима отвязки панель пометила Preview клика по интерактивному элементу карточки как Handled");
            });
        }

        private static List<System.Windows.Controls.CheckBox> AllChecks(DependencyObject node)
        {
            var result = new List<System.Windows.Controls.CheckBox>();
            if (node is System.Windows.Controls.CheckBox cb) result.Add(cb);
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++) result.AddRange(AllChecks(VisualTreeHelper.GetChild(node, i)));
            return result;
        }

        private static void ClickChip(AdminPanelControl panel, string chipName)
        {
            var chip = Find<System.Windows.Controls.Border>(panel, chipName);
            Assert.NotNull(chip);
            chip!.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            FlushUi(panel); // список перегенерируется асинхронно после смены фильтра
        }

        private static void TypeIntoSearch(AdminPanelControl panel, string text)
        {
            var box = Find<TextBox>(panel, "TxtSearch");
            Assert.NotNull(box);
            box!.Text = text;
            FlushUi(panel);
        }

        private static void ClearSearch(AdminPanelControl panel)
        {
            var box = Find<TextBox>(panel, "TxtSearch");
            box!.Clear();
            FlushUi(panel);
        }

        private static List<string> VisibleGroupHeaders(ItemsControl list)
        {
            var result = new List<string>();
            CollectGroups(list, result);
            return result;
        }

        private static void CollectGroups(DependencyObject node, List<string> result)
        {
            // Заголовок группы — TextBlock внутри ContentPresenter GroupItem'а
            // (наш HeaderTemplate). Ищем его по тексту заголовков.
            if (node is ContentPresenter cp && cp.TemplatedParent is GroupItem)
            {
                var tb = FindDescendantTextBlock(cp);
                if (tb != null) result.Add(tb.Text);
                return;
            }
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
                CollectGroups(VisualTreeHelper.GetChild(node, i), result);
        }

        private static TextBlock? FindDescendantTextBlock(DependencyObject node)
        {
            if (node is TextBlock tb) return tb;
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
            {
                var found = FindDescendantTextBlock(VisualTreeHelper.GetChild(node, i));
                if (found != null) return found;
            }
            return null;
        }

        private static Border? FindOfficeCardByPrefix(AdminPanelControl panel, string prefix)
        {
            // Префикс виден в TextBlock «Офис {0}» внутри карточки; ищем карточку,
            // чей DataContext — строка с этим префиксом.
            return FindCardByPrefix(panel.OfficesList, prefix);
        }

        private static Border? FindCardByPrefix(DependencyObject node, string prefix)
        {
            if (node is Border b && b.DataContext is OfficeStatusRow row && row.Prefix == prefix)
                return b;
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
            {
                var found = FindCardByPrefix(VisualTreeHelper.GetChild(node, i), prefix);
                if (found != null) return found;
            }
            return null;
        }

    }
}
