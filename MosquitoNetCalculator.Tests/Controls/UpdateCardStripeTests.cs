using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Tests.App;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Владелец: «вот эта полоска немного не в дизайн — общий дизайн скруглённый,
    /// а полоска квадратная». Цветная полоса типа версии на карточке «Истории
    /// обновлений» обязана повторять скругление карточки, а не торчать квадратом
    /// в её скруглённом углу.
    /// <para>Тест рендерит РЕАЛЬНЫЙ <see cref="UpdatesTabControl"/> (та же разметка
    /// и те же словари тем, что в продукте) и смотрит пиксели: в углах карточки
    /// пикселей полосы быть не должно, а на левой кромке они обязаны быть — иначе
    /// «успех» означал бы просто удалённую полосу.</para>
    /// </summary>
    [Collection("WPF_UI")]
    public class UpdateCardStripeTests
    {
        private const int W = 900;
        private const int H = 400;

        [Fact]
        public void TypeStripe_FollowsTheRoundedCorner_AndStillPaintsTheLeftEdge()
        {
            RunOnSta(() =>
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

                var px = Render(control);

                // Кромка: полоса на месте.
                Assert.True(CountStripe(px, origin.X + 1, origin.Y + cardH / 2, 3, 2, accent) > 0,
                    "type stripe is missing on the card's left edge");

                // Скруглённые углы: ни одного пикселя полосы (квадратный
                // прямоугольник Width=4 закрашивал их целиком).
                Assert.Equal(0, CountStripe(px, origin.X, origin.Y, 5, 3, accent));
                Assert.Equal(0, CountStripe(px, origin.X, origin.Y + cardH - 3, 5, 3, accent));
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

        private static byte[] Render(FrameworkElement element)
        {
            var bitmap = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);

            int stride = W * 4;
            var pixels = new byte[stride * H];
            bitmap.CopyPixels(pixels, stride, 0);
            return pixels;
        }

        /// <summary>Сколько пикселей в прямоугольнике окрашены в цвет полосы (Pbgra32).</summary>
        private static int CountStripe(byte[] pixels, double x0, double y0, int w, int h, Color stripe)
        {
            int hits = 0;
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    int x = (int)Math.Round(x0) + dx;
                    int y = (int)Math.Round(y0) + dy;
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;

                    int i = (y * W + x) * 4;
                    if (pixels[i + 3] < 200) continue;   // прозрачно — рендер не покрыл
                    if (Math.Abs(pixels[i + 2] - stripe.R) <= 10 &&
                        Math.Abs(pixels[i + 1] - stripe.G) <= 10 &&
                        Math.Abs(pixels[i] - stripe.B) <= 10)
                        hits++;
                }
            }

            return hits;
        }

        // ════════════════════════════════════════════════════════════════════
        // STA + темы приложения — тот же каркас, что у остальных UI-тестов
        // ([Collection("WPF_UI")]: один Application на AppDomain).
        // ════════════════════════════════════════════════════════════════════

        private static void RunOnSta(Action action)
        {
            Exception? caught = null;
            var gate = new ManualResetEventSlim(false);

            var thread = new Thread(() =>
            {
                try
                {
                    if (Application.Current != null)
                        AppLifecycleTests.ClearWpfApplicationStatic();

                    try
                    {
                        EnsureAppThemes();
                        action();
                    }
                    finally
                    {
                        AppLifecycleTests.ClearWpfApplicationStatic();
                    }
                }
                catch (Exception ex) { caught = ex; }
                finally { gate.Set(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.True(gate.Wait(TimeSpan.FromSeconds(90)), "STA render test thread did not finish in time");
            thread.Join();

            // ExceptionDispatchInfo, а не throw — иначе теряется исходный стек
            // (throw caught сбрасывает трассировку на эту строку).
            if (caught != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
        }

        private static void EnsureAppThemes()
        {
            string sourceDir = LocateSourceProject();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            string[] dictionaries =
            {
                "Brushes.xaml", "FocusVisualStyles.xaml", "CardStyles.xaml",
                "FontStyles.xaml", "TabStyles.xaml", "ButtonStyles.xaml",
                "InputStyles.xaml", "DataGridStyles.xaml", "InputStyles.RadioButton.xaml",
                "ScrollViewerStyles.xaml", "ContextMenuStyles.xaml", "MiscStyles.xaml",
            };

            foreach (var dictionary in dictionaries)
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(Path.Combine(sourceDir, "Themes", dictionary), UriKind.Absolute)
                });
            }

            // Конвертеры вкладки «Обновления» (в продукте их регистрирует App.xaml).
            app.Resources["BoolToVis"] = new BooleanToVisibilityConverter();
            app.Resources["RussianDateConv"] = new MosquitoNetCalculator.Converters.RussianDateConverter();
            app.Resources["UpdateTypeBrush"] = new MosquitoNetCalculator.Converters.UpdateTypeToBrushConverter();
            app.Resources["UpdateTypeIcon"] = new MosquitoNetCalculator.Converters.UpdateTypeToIconConverter();
            app.Resources["IsMyVersion"] = new MosquitoNetCalculator.Converters.IsMyVersionConverter();
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
