using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// PLAYTEST (throwaway): панель открывается «первым пользователем» — реальный
    /// AdminPanelControl на STA-потоке с реальными темами App.xaml, данными из
    /// чистых конструкторов моделей. Находит дефекты запуска/интеракции, которые
    /// не ловит ни сборка, ни чистые тесты.
    /// <para>WPF_UI — сериализованная коллекция (как у AppLifecycleTests);
    /// приложение с темами поднимает общий самовосстанавливающийся bootstrap
    /// <see cref="Helpers.TestAppThemes"/> и переиспользует его между тестами.</para>
    /// </summary>
    [Collection("WPF_UI")]
    public class AdminPanelPlaytestTests
    {
        /// <summary>STA-поток с приложением и темами — общий bootstrap коллекции.</summary>
        private static void RunOnSta(Action action) => TestAppThemes.RunOnSta(action);

        /// <summary>
        /// Открывает панель, наполняет Rows/StatsRows (как это делает RefreshAsync),
        /// применяет пустые состояния. Возвращает панель для интеракций.
        /// </summary>
        private static AdminPanelControl OpenPanelWithData(
            out List<OfficeStatusRow> rows,
            OfficeStatus? overrideStatus = null)
        {
            TestAppThemes.Ensure();
            var panel = new AdminPanelControl();
            // Без Measure/Arrange у контрола нет визуального дерева (шаблон не
            // применён) — эмулируем размещение в окне, как при реальном показе.
            panel.Measure(new Size(900, 800));
            panel.Arrange(new Rect(0, 0, 900, 800));
            panel.UpdateLayout();

            var now = DateTimeOffset.Now;
            OfficeDeviceRow Dev(
                string name,
                string ver,
                OfficeStatus st,
                DateTimeOffset at,
                DateTimeOffset? confirmedAt = null) => new()
            {
                DeviceId = "id-" + name,
                DeviceName = name,
                Version = ver,
                LastReportAt = at,
                LocationConfirmedAt = confirmedAt,
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
                    DeviceCount = 1, Devices = new[] { Dev("DESKTOP-K4RDFUN", "3.49.0", OfficeStatus.UpToDate, now.AddMinutes(-10), now.AddDays(-2)) } },
                new() { Prefix = "3", LocationName = LocationOptions.All.First(o => o.Prefix == "3").LocationName,
                    Status = OfficeStatus.NoData, IsCurrentOffice = false, DeviceCount = 1,
                    Devices = new[] { Dev("DESKTOP-GHOST", "3.44.0", OfficeStatus.NoData, now.AddDays(-8)) } },
                new() { Prefix = "4", LocationName = LocationOptions.All.First(o => o.Prefix == "4").LocationName,
                    Status = OfficeStatus.NoData, IsCurrentOffice = false, DeviceCount = 0, Devices = Array.Empty<OfficeDeviceRow>() },
            };

            panel.ReplaceRows(list);
            panel.UpdateSummary(list);

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
                Assert.True(Find<Button>(panel, "BtnUnbindMode")!.IsEnabled);
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
                Assert.False(Find<Button>(panel, "BtnUnbindMode")!.IsEnabled);
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
        public void Playtest_LocationCheckHint_RendersOnlyWhenConfirmationIsStale()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out _);
                FlushUi(panel);

                // Текст читаем ИЗ ОТРИСОВАННЫХ TextBlock'ов, а не из модели:
                // тест ловит и логику, и биндинг в XAML (DataTrigger на пустой
                // текст должен скрывать подсказку, а не оставлять пустую строку).
                var hints = AllChecks(panel)
                    .Select(b => (Device: (OfficeDeviceRow)b.DataContext, Chip: (StackPanel)FindChipHint(b)!))
                    .ToDictionary(
                        x => x.Device,
                        x => x.Chip.Children.OfType<TextBlock>()
                            .FirstOrDefault(tb => tb.Text.StartsWith("локация", StringComparison.Ordinal))?.Text ?? "");

                Assert.NotEmpty(hints);

                foreach (var (device, hint) in hints)
                {
                    bool stale = device.LocationConfirmedAt == null ||
                                 DateTimeOffset.Now - device.LocationConfirmedAt.Value > TimeSpan.FromDays(30);
                    Assert.Equal(stale, hint.Length > 0);
                }

                // В фикстуре есть оба случая: старые отчёты без отметки (текст
                // «локация не подтверждена») и свежее подтверждение офиса 5 —
                // пустой текст ничего не рисует, поэтому чип свежего ПК не шумит.
                Assert.Contains(hints.Values, h => h == "локация не подтверждена");
                Assert.Contains(hints.Values, h => h.Length == 0);
            });
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

        [Fact]
        public void OfficesScrollViewer_ExistsAndIsNamed()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out var rows);
                Assert.NotNull(panel.OfficesScrollViewer);
                panel.OfficesScrollViewer.ScrollToVerticalOffset(150);
                panel.ReplaceRows(rows);
                Assert.Equal(0, panel.OfficesScrollViewer.VerticalOffset);
            });
        }

        [Fact]
        public void TxtSummaryBadge_DisplaysPercentageCalculation()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out var _);
                // OpenPanelWithData creates:
                // Row 1: 3 devices (2 UpToDate, 1 Outdated)
                // Row 2: 1 device (1 Outdated)
                // Row 3: 1 device (1 UpToDate)
                // Row 4: 1 device (1 NoData)
                // Row 5: 0 devices
                // Total known: 6; UpToDate: 3. Expected percentage: 3 * 100 / 6 = 50%
                Assert.Equal("50%", panel.TxtSummaryBadge.Text);
                Assert.Equal("3 из 6 устройств актуальны", panel.TxtSummary.Text);

                // 100% when all devices up-to-date
                var allUpToDate = new List<OfficeStatusRow>
                {
                    new()
                    {
                        Prefix = "1",
                        LocationName = "Офис 1",
                        DeviceCount = 2,
                        Devices = new[]
                        {
                            new OfficeDeviceRow { DeviceId = "d1", Status = OfficeStatus.UpToDate },
                            new OfficeDeviceRow { DeviceId = "d2", Status = OfficeStatus.UpToDate }
                        }
                    }
                };
                panel.UpdateSummary(allUpToDate);
                Assert.Equal("100%", panel.TxtSummaryBadge.Text);
                Assert.Equal("2 из 2 устройств актуальны", panel.TxtSummary.Text);

                // 0% when none are up-to-date
                var noneUpToDate = new List<OfficeStatusRow>
                {
                    new()
                    {
                        Prefix = "1",
                        LocationName = "Офис 1",
                        DeviceCount = 2,
                        Devices = new[]
                        {
                            new OfficeDeviceRow { DeviceId = "d1", Status = OfficeStatus.Outdated },
                            new OfficeDeviceRow { DeviceId = "d2", Status = OfficeStatus.NoData }
                        }
                    }
                };
                panel.UpdateSummary(noneUpToDate);
                Assert.Equal("0%", panel.TxtSummaryBadge.Text);
                Assert.Equal("0 из 2 устройств актуальны", panel.TxtSummary.Text);

                // string.Empty when 0 known devices
                var zeroDevices = new List<OfficeStatusRow>
                {
                    new() { Prefix = "1", LocationName = "Офис 1", DeviceCount = 0, Devices = Array.Empty<OfficeDeviceRow>() }
                };
                panel.UpdateSummary(zeroDevices);
                Assert.Equal(string.Empty, panel.TxtSummaryBadge.Text);
                Assert.Equal("0 из 0 устройств актуальны", panel.TxtSummary.Text);
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

        [Fact]
        public void Playtest_BannersCollapsed_DoesNotOccupyVerticalSpace()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out var _);
                panel.Measure(new Size(800, 600));
                panel.Arrange(new Rect(0, 0, 800, 600));
                panel.UpdateLayout();

                var bannersHost = (StackPanel)panel.BannerNotConfigured.Parent;
                Assert.Equal(0, bannersHost.ActualHeight);
            });
        }

        [Fact]
        public void Playtest_OfficeCard_OuterBorderUsesRadiusCard_ConcentricWithInnerStripe()
        {
            RunOnSta(() =>
            {
                var panel = OpenPanelWithData(out var _);
                var card = FindOfficeCardByPrefix(panel, "1");
                Assert.NotNull(card);

                var expectedRadius = (CornerRadius)Application.Current.Resources["Radius.Card"];
                Assert.Equal(expectedRadius, card!.CornerRadius);
            });
        }
    }
}
