using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MosquitoNetCalculator.Converters;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Владелец: «вот эта полоска немного не в дизайн — общий дизайн скруглённый,
    /// а полоска квадратная». Цветная полоса слева на карточке (тип версии в
    /// «Истории обновлений», статус офиса в админ-панели) обязана повторять
    /// скругление карточки, а не торчать квадратом в её скруглённом углу.
    /// <para>Тесты рендерят РЕАЛЬНЫЕ контролы (та же разметка и те же словари тем,
    /// что в продукте) и смотрят пиксели: в углах карточки пикселей полосы быть не
    /// должно, а на левой кромке они обязаны быть — иначе «успех» означал бы просто
    /// удалённую полосу.</para>
    /// </summary>
    [Collection("WPF_UI")]
    public class CardStripeCornerTests
    {
        private const int W = 900;
        private const int H = 400;
        /// <summary>Админ-панель: карточки идут ниже шапки/чипов/прогресса — нужен высокий холст.</summary>
        private const int PanelH = 800;

        [Fact]
        public void UpdateCard_TypeStripe_FollowsTheRoundedCorner_AndStillPaintsTheLeftEdge()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var accent = ((SolidColorBrush)Application.Current!.Resources["Accent"]).Color;

                var card = new UpdateItem
                {
                    Date = new DateTime(2026, 9, 14),
                    Version = "9.99.0",
                    Type = "Улучшение",
                    Title = "Полоса типа на карточке",
                    Changes = { "Текст изменения для раскрытого тела карточки." },
                    IsExpanded = true,
                };

                var control = new UpdatesTabControl
                {
                    DataContext = new { Updates = new List<UpdateItem> { card } },
                    Width = W,
                    Height = H,
                };
                var size = new Size(W, H);
                control.Measure(size);
                control.Arrange(new Rect(size));
                control.UpdateLayout();

                var cardBorder = FindUpdateCard(control);
                var origin = cardBorder.TransformToAncestor(control).Transform(new Point(0, 0));
                double cardW = cardBorder.ActualWidth, cardH = cardBorder.ActualHeight;

                Assert.True(cardW > 200, $"update card was not laid out (w={cardW:F0})");
                Assert.True(cardH >= 24, $"update card is too short to test its corners (h={cardH:F0})");

                var shot = Shot.Of(control, W, H);
                AssertCardStripe(shot, origin, cardW, cardH, accent,
                    "update card (type stripe)");
            });
        }

        [Fact]
        public void OfficeCard_StatusStripe_FollowsTheRoundedCorner_AndStillPaintsTheLeftEdge()
        {
            TestAppThemes.RunOnSta(() =>
            {
                // Тот же источник цвета, что и в продукте: полоса = кисть темы
                // «Warning» для устаревшего офиса (OfficeStatusToStripeBrushConverter).
                var stripe = ((SolidColorBrush)new OfficeStatusToStripeBrushConverter()
                    .Convert(OfficeStatus.Outdated, typeof(Brush), null!, CultureInfo.InvariantCulture)).Color;

                var now = DateTimeOffset.Now;
                var row = new OfficeStatusRow
                {
                    Prefix = "2",
                    LocationName = "Тестовый офис",
                    Status = OfficeStatus.Outdated,
                    LastReportAt = now.AddMinutes(-30),
                    DeviceCount = 1,
                    Devices = new[]
                    {
                        new OfficeDeviceRow
                        {
                            DeviceId = "id-1",
                            DeviceName = "DESKTOP-TEST",
                            Version = "3.49.0",
                            Status = OfficeStatus.Outdated,
                            LastReportAt = now.AddMinutes(-30),
                        },
                    },
                };

                var panel = new AdminPanelControl();
                panel.Measure(new Size(W, PanelH));
                panel.Arrange(new Rect(0, 0, W, PanelH));
                panel.UpdateLayout();
                panel.ReplaceRows(new List<OfficeStatusRow> { row });
                FlushUi(panel);

                // Полоса — левая граница обёртки карточки, поэтому карточка =
                // визуальный родитель этой границы.
                var frame = FindStripeFrame(panel);
                var card = VisualTreeHelper.GetParent(frame) as Border;
                Assert.NotNull(card);

                var origin = card!.TransformToAncestor(panel).Transform(new Point(0, 0));
                double cardW = card.ActualWidth, cardH = card.ActualHeight;
                Assert.True(cardW > 200, $"office card was not laid out (w={cardW:F0})");
                Assert.True(cardH >= 24, $"office card is too short to test its corners (h={cardH:F0})");

                var shot = Shot.Of(panel, W, PanelH);
                AssertCardStripe(shot, origin, cardW, cardH, stripe, "office card (status stripe)");
            });
        }

        /// <summary>Карточка версии — Border со стилем UpdateCardStyle из ресурсов контрола.</summary>
        private static Border FindUpdateCard(UpdatesTabControl control)
        {
            var style = (Style)control.FindResource("UpdateCardStyle");
            var stack = new Stack<DependencyObject>(new DependencyObject[] { control });

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node is Border border && ReferenceEquals(border.Style, style))
                    return border;

                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                    stack.Push(VisualTreeHelper.GetChild(node, i));
            }

            throw new InvalidOperationException(
                "UpdateCardStyle Border was not found in the render tree — the card did not render.");
        }

        /// <summary>Обёртка полосы карточки офиса — Border только с левой границей 4px.</summary>
        private static Border FindStripeFrame(AdminPanelControl panel)
        {
            var stack = new Stack<DependencyObject>(new DependencyObject[] { panel });

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node is Border b &&
                    Math.Abs(b.BorderThickness.Left - 4) < 0.01 &&
                    b.BorderThickness.Top == 0 && b.BorderThickness.Right == 0 && b.BorderThickness.Bottom == 0)
                    return b;

                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                    stack.Push(VisualTreeHelper.GetChild(node, i));
            }

            throw new InvalidOperationException(
                "Office-card stripe frame (4px left border) not found — the card must draw its " +
                "status stripe as a rounded left border, not as a square rectangle.");
        }

        /// <summary>
        /// Прогоняет очередь диспетчера до простоя и применяет layout — ItemsControl
        /// создаёт контейнеры карточек асинхронно, дерево читается только после этого.
        /// </summary>
        private static void FlushUi(AdminPanelControl panel)
        {
            panel.Measure(new Size(W, PanelH));
            panel.Arrange(new Rect(0, 0, W, PanelH));
            for (int i = 0; i < 2; i++)
            {
                panel.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
                panel.UpdateLayout();
            }
        }

        /// <summary>
        /// Снимок рендера контрола: пиксели (Pbgra32) + размеры холста.
        /// Ширина нужна, чтобы перейти от координаты к индексу в буфере.
        /// </summary>
        private sealed class Shot
        {
            public byte[] Pixels = Array.Empty<byte>();
            public int Width;
            public int Height;

            public static Shot Of(FrameworkElement element, int width, int height)
            {
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(element);

                int stride = width * 4;
                var pixels = new byte[stride * height];
                bitmap.CopyPixels(pixels, stride, 0);

                return new Shot { Pixels = pixels, Width = width, Height = height };
            }

            /// <summary>Сколько пикселей в прямоугольнике окрашены в цвет полосы.</summary>
            public int CountStripe(double x0, double y0, int w, int h, Color stripe)
            {
                int hits = 0;
                for (int dy = 0; dy < h; dy++)
                {
                    for (int dx = 0; dx < w; dx++)
                    {
                        int x = (int)Math.Round(x0) + dx;
                        int y = (int)Math.Round(y0) + dy;
                        if (x < 0 || y < 0 || x >= Width || y >= Height) continue;

                        int i = (y * Width + x) * 4;
                        if (Pixels[i + 3] < 200) continue;   // прозрачно — рендер не покрыл
                        if (Math.Abs(Pixels[i + 2] - stripe.R) <= 10 &&
                            Math.Abs(Pixels[i + 1] - stripe.G) <= 10 &&
                            Math.Abs(Pixels[i] - stripe.B) <= 10)
                            hits++;
                    }
                }

                return hits;
            }
        }

        /// <summary>
        /// Общая проверка для обоих видов карточек: полоса есть на левой кромке и
        /// НЕ закрашивает скруглённые углы карточки.
        /// </summary>
        private static void AssertCardStripe(
            Shot shot, Point origin, double cardW, double cardH, Color stripe, string what)
        {
            Assert.True(origin.X + cardW <= shot.Width && origin.Y + cardH <= shot.Height,
                $"{what}: card is outside the rendered canvas — the pixel check would be meaningless");

            // Кромка: полоса на месте (иначе тест «прошёл» бы на пустой карточке).
            Assert.True(shot.CountStripe(origin.X + 1, origin.Y + cardH / 2, 3, 2, stripe) > 0,
                $"{what}: stripe is missing on the card's left edge");

            // Скруглённые углы: ни одного пикселя полосы (квадратный
            // прямоугольник Width=4 закрашивал их целиком).
            Assert.Equal(0, shot.CountStripe(origin.X, origin.Y, 5, 3, stripe));
            Assert.Equal(0, shot.CountStripe(origin.X, origin.Y + cardH - 3, 5, 3, stripe));
        }

    }
}
