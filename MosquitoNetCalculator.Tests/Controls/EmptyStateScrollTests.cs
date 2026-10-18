using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Жалоба владельца: в «Расчёте» панель с товарами (каталог чипов пустого
    /// состояния) не прокручивалась. Карточка позиций имеет высоту строки
    /// таблицы, а пять категорий чипов в неё не влезают — поэтому нижние
    /// категории обрезались краем карточки, и доскроллить их было невозможно.
    /// <para>
    /// Тест рендерит РЕАЛЬНЫЙ <see cref="OrderItemsControl"/> на STA с темами
    /// приложения (та же разметка и те же словари, что в продукте) и проверяет:
    /// (1) у каталога есть ScrollViewer, чей контент выше области просмотра —
    /// список прокручивается, а не обрезается; (2) шапка состояния закреплена
    /// НАД областью прокрутки и видна без прокрутки; (3) хвостовая подсказка
    /// лежит ниже сгиба — то есть без прокрутки её не видно, а с прокруткой она
    /// достижима, потому что живёт внутри того же ScrollViewer.
    /// </para>
    /// </summary>
    [Collection("WPF_UI")]
    public class EmptyStateScrollTests
    {
        /// <summary>Ширина карточки позиций на окне 1200px с нав-рельсом 160px.</summary>
        private const double CardW = 900;

        /// <summary>Высота строки таблицы (реалистичная для окна 1200x760).</summary>
        private const double CardH = 320;

        private static IEnumerable<DependencyObject> VisualSubtree(DependencyObject root)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var d in VisualSubtree(child))
                    yield return d;
            }
        }

        /// <summary>Пустое состояние с полным каталогом чипов, измеренное и расположенное.</summary>
        private static OrderItemsControl BuildEmptyState()
        {
            var control = new OrderItemsControl { Width = CardW, Height = CardH };
            control.PopulateProductChips(
                ProductCatalog.UserGroups.SelectMany(g => g.Products).ToList());
            control.Empty.Visibility = Visibility.Visible;
            control.Measure(new Size(CardW, CardH));
            control.Arrange(new Rect(0, 0, CardW, CardH));
            control.UpdateLayout();
            return control;
        }

        /// <summary>ScrollViewer, внутри которого лежит ProductChipsList (не скролл самой таблицы).</summary>
        private static ScrollViewer? CatalogScroll(OrderItemsControl control) =>
            VisualSubtree(control).OfType<ScrollViewer>().FirstOrDefault(sv =>
                sv.Content is StackPanel panel && VisualSubtree(panel)
                    .OfType<ItemsControl>()
                    .Any(i => i.Name == "ProductChipsList"));

        [Fact]
        public void Catalog_Scrolls_InsteadOfBeingClippedByTheCard()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var control = BuildEmptyState();

                var scroll = CatalogScroll(control);
                Assert.True(scroll != null,
                    "Каталог чипов лежит вне ScrollViewer — карточка обрежет его нижний край.");

                Assert.True(scroll!.ViewportHeight > 0, "Область просмотра каталога нулевой высоты.");
                Assert.True(scroll.ExtentHeight > scroll.ViewportHeight + 24,
                    $"Каталог не прокручивается: контент {scroll.ExtentHeight:F0}px не выше области " +
                    $"просмотра {scroll.ViewportHeight:F0}px — нижние категории снова обрезаны.");

                // Хвостовая подсказка лежит ниже сгиба: без прокрутки её не видно,
                // а внутри ScrollViewer она достижима.
                var footer = VisualSubtree(control).OfType<TextBlock>()
                    .FirstOrDefault(t => InlineText(t).Contains("или используйте"));
                Assert.True(footer != null, "Хвостовая подсказка каталога не найдена.");

                var content = (UIElement)scroll.Content;
                var offsetInContent = footer!.TransformToAncestor(content)
                    .Transform(new Point(0, 0)).Y;
                Assert.True(offsetInContent > scroll.ViewportHeight,
                    $"Подсказка лежит на {offsetInContent:F0}px при сгибе {scroll.ViewportHeight:F0}px — " +
                    "ожидалось, что хвост каталога уходит под сгиб и добирается прокруткой.");
            });
        }

        [Fact]
        public void StateHeader_StaysPinnedAboveTheScrollArea()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var control = BuildEmptyState();
                var scroll = CatalogScroll(control);
                Assert.True(scroll != null, "ScrollViewer каталога не найден.");

                var title = VisualSubtree(control).OfType<TextBlock>()
                    .FirstOrDefault(t => t.Text == "Выберите товар");
                Assert.True(title != null, "Заголовок пустого состояния не найден.");

                var titleBottom = title!.TransformToAncestor(control)
                    .Transform(new Point(0, title.ActualHeight)).Y;
                var scrollTop = scroll!.TransformToAncestor(control)
                    .Transform(new Point(0, 0)).Y;

                Assert.True(titleBottom > 0, "Заголовок состояния вышел за верх карточки.");
                Assert.True(titleBottom <= scrollTop + 0.5,
                    $"Шапка ({titleBottom:F0}px) должна заканчиваться ДО начала прокрутки ({scrollTop:F0}px) — " +
                    "иначе пока пользователь листает каталог, подсказка «нажмите на товар» уезжает вместе с ним.");
            });
        }

        /// <summary>TextBlock с <c>Run</c>-детьми держит текст в Inline, а не в Text
        /// (у пустого TextBlock.Text бывает string.Empty, а не null — поэтому
        /// конкатенируем оба источника).</summary>
        private static string InlineText(TextBlock block) =>
            (block.Text ?? string.Empty) +
            string.Concat(block.Inlines.OfType<Run>().Select(r => r.Text));
    }
}
