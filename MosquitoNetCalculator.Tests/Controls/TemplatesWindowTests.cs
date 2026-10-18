using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// STA-тесты TemplatesWindow (v3.54). Минимальный страж: окно
    /// конструируется с реальными темами приложения — любой отсутствующий
    /// StaticResource/DynamicResource в разметке или BuildGallery роняет тест
    /// с реальным стеком, а не молча (в рантайме ошибку конструктора окна
    /// глотает DispatcherUnhandledException, и кнопка выглядит «мёртвой»).
    /// </summary>
    [Collection("WPF_UI")]
    public class TemplatesWindowTests
    {
        [Fact]
        public void Constructor_DoesNotThrow()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var window = new TemplatesWindow(null!);
                try
                {
                    Assert.NotNull(window);
                    Assert.Equal("Шаблоны", window.Title);
                    // Витрина построена: по карточке на шаблон каталога.
                    Assert.True(window.GalleryPanel.Children.Count > 0, "витрина пуста");
                }
                finally
                {
                    window.Close();
                }
            });
        }

        // ── Регрессия v3.54-fix2: «Выберите тип сетки.» на заполненной форме ──
        //
        // Начальная установка SelectedIndex происходит ДО подписки на
        // SelectionChanged, поэтому событие не срабатывает и _state оставался
        // GridProductIndex = −1 («не выбран»), хотя в списке уже показан Anwis.
        // Валидация честно требовала «Выберите тип сетки.» при нажатии
        // «Добавить в заказ». Поля строим через internal-шов (цвета — fallback
        // «Белый»), MainWindow в тест-хосте не создаётся.

        private static object? GetPrivate(object owner, string name) =>
            owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);

        /// <summary>Первый ComboBox панели — список «Тип» (добавляется первым).</summary>
        private static ComboBox TypeCombo(StackPanel topPanel) =>
            topPanel.Children.OfType<Grid>().First().Children.OfType<ComboBox>().First();

        [Fact]
        public void RebuildGridTopFields_SyncsStateBeforeAnyInteraction()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var window = new TemplatesWindow(null!);
                try
                {
                    var state = (OrderTemplateService)GetPrivate(window, "_state")!;
                    Assert.Equal(-1, state.GridProductIndex); // предусловие: тип ещё не выбран

                    var top = new StackPanel();
                    typeof(TemplatesWindow)
                        .GetField("_gridTopPanel", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(window, top);
                    window.RebuildGridTopFields(_ => null); // цвета → фолбэк «Белый»

                    // ЯДРО БАГА: список показывает Anwis (SelectedIndex 0), но
                    // начальная установка не синхронизировала _state — валидация
                    // требовала «Выберите тип сетки.» на заполненной форме.
                    Assert.Equal(0, state.GridProductIndex);
                    var cmbType = TypeCombo(top);
                    Assert.Equal(0, cmbType.SelectedIndex);
                    Assert.Equal(OrderTemplateService.GridProductChoices[0], (string)cmbType.SelectedItem!);

                    // Режим Anwis синхронизирован тем же способом.
                    Assert.Equal(AnwisSizeService.DefaultMode, state.GridAnwisMode);

                    // Пользователь вводит размеры и количество — валидация проходит
                    // без единого касания выпадающих списков (сценарий бага).
                    state.GridWidth = 500;
                    state.GridHeight = 1500;
                    state.GridQuantity = 1;
                    state.PsulEnabled = false;
                    state.DeliveryEnabled = false;
                    Assert.Null(state.GetValidationError());
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void RebuildGridTopFields_NonAnwisType_KeepsIndexInSync()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var window = new TemplatesWindow(null!);
                try
                {
                    var state = (OrderTemplateService)GetPrivate(window, "_state")!;

                    var top = new StackPanel();
                    typeof(TemplatesWindow)
                        .GetField("_gridTopPanel", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(window, top);

                    // Пользователь выбрал «На навесах» (не Anwis) — панель
                    // пересобирается без строки «Режим», индекс не сбрасывается.
                    int index = Array.IndexOf(OrderTemplateService.GridProductChoices, "На навесах");
                    state.GridProductIndex = index;
                    window.RebuildGridTopFields(_ => null);

                    Assert.Equal(index, state.GridProductIndex);
                    Assert.Equal(index, TypeCombo(top).SelectedIndex);

                    state.GridWidth = 700;
                    state.GridHeight = 1200;
                    state.GridQuantity = 1;
                    state.PsulEnabled = false;
                    state.DeliveryEnabled = false;
                    Assert.Null(state.GetValidationError());

                    var specs = state.BuildItemSpecs(
                        OrderTemplateService.All.Single(t => t.Id == "window"),
                        (_, _) => 0);
                    var grid = specs.Single(s => s.RowKey == "grid");
                    Assert.Equal("На навесах", grid.Type);
                    Assert.Null(grid.AnwisMode);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        // ── Регрессия по фидбеку владельца: «иконки какие-то… они вообще
        // никак не связаны с тем, о чём речь» ──
        //
        // Карточки витрины обязаны рисовать ВЕКТОРНУЮ пиктограмму товара
        // (Path с геометрией 24×24: окно с расстекловкой, блок «дверь+окно»,
        // профиль рамы, три створки «француза»), а не шрифтовой глиф Segoe
        // Fluent Icons — тот показывал что угодно, только не товар.
        // Страж: в слоте иконки каждой карточки лежит Path, и рисунки
        // у карточек разные.

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            // Логическое + визуальное дерево: карточка — Button с ещё не
            // применённым шаблоном, у него визуальных детей нет, а содержимое
            // (слот иконки) живёт в логическом дереве Content'а.
            var seen = new HashSet<DependencyObject>();
            var stack = new Stack<DependencyObject>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var node = stack.Pop();

                // Логическое дерево отдаёт и не-визуальные узлы (RowDefinition,
                // Setter) — VisualTreeHelper на них бросает.
                if (node is Visual)
                {
                    int visualCount = VisualTreeHelper.GetChildrenCount(node);
                    for (int i = 0; i < visualCount; i++)
                    {
                        var child = VisualTreeHelper.GetChild(node, i);
                        if (seen.Add(child)) stack.Push(child);
                    }
                }

                foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                {
                    if (seen.Add(child)) stack.Push(child);
                }
            }

            return seen;
        }

        [Fact]
        public void GalleryCards_UseVectorPictograms_NotFontGlyphs()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var window = new TemplatesWindow(null!);
                try
                {
                    var iconStyle = window.FindResource("TemplateCardIcon");
                    var cards = Descendants(window.GalleryPanel).OfType<Button>().ToList();
                    Assert.Equal(OrderTemplateService.All.Count(), cards.Count);

                    var drawings = new List<string>();
                    for (int i = 0; i < cards.Count; i++)
                    {
                        var iconSlot = Descendants(cards[i]).OfType<Border>()
                            .FirstOrDefault(b => ReferenceEquals(b.Style, iconStyle));
                        Assert.True(iconSlot != null, $"карточка {i}: слот иконки не найден");

                        var pictogram = iconSlot!.Child as System.Windows.Shapes.Path;
                        Assert.True(pictogram != null,
                            $"карточка {i}: в слоте иконки не Path (шрифтовой глиф вернулся?)");

                        var geometry = pictogram!.Data;
                        Assert.True(geometry != null &&
                                    geometry.ToString()!.IndexOf('M') >= 0,
                            $"карточка {i}: пиктограмма без геометрии");
                        Assert.NotNull(pictogram.Stroke); // контур красится токеном темы

                        drawings.Add(geometry!.ToString()!);
                    }

                    // У каждой карточки — СВОЙ рисунок, а не одинаковая заглушка.
                    Assert.Equal(cards.Count, drawings.Distinct(StringComparer.Ordinal).Count());
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void OpenChecklist_AllFourTemplates_BuildsChecklistWithoutThrowing()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var window = new TemplatesWindow(null!);
                try
                {
                    var openMethod = typeof(TemplatesWindow)
                        .GetMethod("OpenChecklist", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var backMethod = typeof(TemplatesWindow)
                        .GetMethod("ShowGalleryView", BindingFlags.Instance | BindingFlags.NonPublic)!;

                    foreach (var template in OrderTemplateService.All)
                    {
                        openMethod.Invoke(window, new object[] { template });

                        Assert.Equal(Visibility.Visible, window.ChecklistView.Visibility);
                        Assert.Equal(Visibility.Collapsed, window.GalleryView.Visibility);
                        Assert.Equal(template.Rows.Count, window.ChecklistPanel.Children.Count);

                        // Проверяем возврат на витрину
                        backMethod.Invoke(window, null);
                        Assert.Equal(Visibility.Collapsed, window.ChecklistView.Visibility);
                        Assert.Equal(Visibility.Visible, window.GalleryView.Visibility);
                    }
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void GalleryView_LayoutAndSizing_IsCompactAndTidy()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var window = new TemplatesWindow(null!);
                try
                {
                    // Окно настроено на автоматическую подгонку по высоте контента,
                    // без хардкода 700px и гигантской пустоты внизу.
                    Assert.Equal(SizeToContent.Height, window.SizeToContent);
                    Assert.True(double.IsNaN(window.Height));

                    // Каждая карточка содержит горизонтальную раскладку с 3 колонками:
                    // [иконка 36×36] [название — описание + чипы всех позиций] [бейдж N поз. + шеврон]
                    var cards = window.GalleryPanel.Children.OfType<Button>().ToList();
                    Assert.Equal(OrderTemplateService.All.Count(), cards.Count);

                    foreach (var card in cards)
                    {
                        var grid = Assert.IsType<Grid>(card.Content);
                        Assert.Equal(3, grid.ColumnDefinitions.Count);

                        // Проверяем наличие чипов товаров внутри центральной колонки
                        var contentStack = Assert.IsType<StackPanel>(grid.Children[1]);
                        var tagsPanel = Assert.IsType<WrapPanel>(contentStack.Children[1]);
                        Assert.True(tagsPanel.Children.Count > 0);

                        // Проверяем правую колонку: бейдж и шеврон
                        var rightStack = Assert.IsType<StackPanel>(grid.Children[2]);
                        var badge = Assert.IsType<Border>(rightStack.Children[0]);
                        var badgeText = Assert.IsType<TextBlock>(badge.Child);
                        Assert.Contains("поз.", badgeText.Text);
                    }

                    // Карточка шаблона «Рама» (8 позиций) обязана содержать теги со множителем (Отлив ×2, Козырёк ×2)
                    var frameCard = cards.First(c => System.Windows.Automation.AutomationProperties.GetName(c).Contains("Рама"));
                    var frameGrid = (Grid)frameCard.Content;
                    var frameTags = (WrapPanel)((StackPanel)frameGrid.Children[1]).Children[1];
                    var tagTexts = frameTags.Children.OfType<Border>()
                        .Select(b => ((TextBlock)b.Child).Text)
                        .ToList();

                    Assert.Contains("Отлив ×2", tagTexts);
                    Assert.Contains("Козырёк ×2", tagTexts);
                    Assert.Contains("Anwis", tagTexts);
                    Assert.Contains("Брус", tagTexts);
                    Assert.Contains("Короб", tagTexts);
                    Assert.Contains("Доставка", tagTexts);

                    // Проверяем переключение в чек-лист и обратно:
                    // В чек-листе высота фиксируется (700px) для скролла, а при возврате
                    // снова сбрасывается в SizeToContent.Height и Height = NaN.
                    var openMethod = typeof(TemplatesWindow)
                        .GetMethod("OpenChecklist", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var backMethod = typeof(TemplatesWindow)
                        .GetMethod("ShowGalleryView", BindingFlags.Instance | BindingFlags.NonPublic)!;

                    openMethod.Invoke(window, new object[] { OrderTemplateService.All.First() });
                    Assert.Equal(SizeToContent.Manual, window.SizeToContent);
                    Assert.True(window.Height >= 700);

                    backMethod.Invoke(window, null);
                    Assert.Equal(SizeToContent.Height, window.SizeToContent);
                    Assert.True(double.IsNaN(window.Height));
                }
                finally
                {
                    window.Close();
                }
            });
        }
    }
}
