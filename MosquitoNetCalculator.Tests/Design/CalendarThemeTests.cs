using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Календарь DatePicker тематизирован целиком (разбор: раньше стилизовали
    /// только сам Calendar — фон/рамку, — а внутренние части CalendarItem,
    /// CalendarDayButton и CalendarButton рисовал дефолтный шаблон Aero2 с
    /// жёстко вшитыми светлыми кистями, и на тёмной теме попап выглядел белым
    /// квадратом из другой ОС).
    ///
    /// <para>Проверяется НАСТОЯЩИЙ календарь, которым пользуется DatePicker:
    /// его берут из PART_Popup. Это принципиально — ребёнком попапа обязан быть
    /// сам Calendar, потому что DatePicker читает календарь как
    /// popup.Child as Calendar и любой другой контейнер молча заменяет своим,
    /// без стиля (замер: именно так пропадала вся тематизация).</para>
    ///
    /// <para>Стражи: (1) 42 клетки дня получили стиль через
    /// Calendar.CalendarDayButtonStyle (стили из ресурсов приложения и из
    /// ресурсов шаблона до этих кнопок не доходят — проверено замером), у
    /// каждой рамка «Bd» с радиусом-пилюлей; (2) 7 подписей дней приглушены
    /// (значит, найден компонентный ключ DayTitleTemplate); (3) selected-день
    /// залит акцентом, шрифт — вшитый Inter; (4) 12 клеток года получают
    /// CalendarButtonStyle при переходе в режим года; (5) хром попапа
    /// (поверхность/рамка/скругление) рисует шаблон самого Calendar.</para>
    /// </summary>
    [Collection("WPF_UI")]
    public class CalendarThemeTests
    {
        private static Style CalendarStyleFromPickerStyle(Application app)
        {
            var pickerStyle = (Style)app.Resources[typeof(DatePicker)];
            Assert.True(pickerStyle != null,
                "Неявный стиль DatePicker не найден в ресурсах приложения");
            var setter = pickerStyle.Setters
                .OfType<Setter>()
                .FirstOrDefault(s => s.Property == DatePicker.CalendarStyleProperty);
            Assert.True(setter != null,
                "В стиле DatePicker нет сеттера CalendarStyle");
            return (Style)setter.Value!;
        }

        private static FrameworkElement? FindNamed(DependencyObject root, string name) =>
            FindDescendants<FrameworkElement>(root).FirstOrDefault(e => e.Name == name);

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed) yield return typed;
                foreach (var nested in FindDescendants<T>(child))
                    yield return nested;
            }
        }

        [Fact]
        public void OpenCalendar_BuildsThemedParts_WithPillDaysAndInter()
        {
            TestAppThemes.RunOnSta(() =>
            {
                try
                {
                    RunAssertions();
                }
                catch (Exception ex)
                {
                    // Rethrow STA-хелпера теряет исходный стек — заворачиваем.
                    throw new InvalidOperationException("CALENDAR: " + ex, ex);
                }
            });
        }

        private static void RunAssertions()
        {
            var app = Application.Current;

            var picker = new DatePicker
            {
                Style = (Style)app.Resources[typeof(DatePicker)],
                DisplayDate = new DateTime(2025, 5, 15),
                SelectedDate = new DateTime(2025, 5, 15),
            };
            picker.ApplyTemplate();
            Assert.True(picker.Template != null,
                "У DatePicker не применился шаблон (неявный стиль не подхватился)");

            var calendarStyle = CalendarStyleFromPickerStyle(app);
            var popup = (Popup)picker.Template.FindName("PART_Popup", picker);
            Assert.True(popup != null, "PART_Popup не найден в шаблоне DatePicker");

            // Именно этот экземпляр использует DatePicker: контейнер вместо
            // Calendar фреймворк заменит своим календарём — без стиля.
            var calendar = popup.Child as Calendar;
            Assert.True(calendar != null,
                $"Ребёнок попапа — {popup.Child?.GetType().Name ?? "null"}, а не Calendar: " +
                "DatePicker заменит его своим календарём без стиля");
            Assert.True(ReferenceEquals(calendar!.Style, calendarStyle),
                "Календарь попапа не получил CalendarStyle из стиля DatePicker");

            Layout(calendar);

            // (1) Клетки дня: ровно 42, у каждой МОЙ стиль и пилюля из токена.
            var dayButtons = FindDescendants<CalendarDayButton>(calendar).ToList();
            Assert.True(dayButtons.Count == 42,
                $"Клеток дня: {dayButtons.Count} (ожидалось 42 — Calendar не наполнил MonthView)");
            Assert.True(calendar.CalendarDayButtonStyle != null,
                "В стиле Calendar не задан CalendarDayButtonStyle");

            var pill = (CornerRadius)app.Resources["Radius.Pill"];
            foreach (var button in dayButtons)
            {
                Assert.True(ReferenceEquals(button.Style, calendar.CalendarDayButtonStyle),
                    $"Клетка дня «{button.Content}» не получила CalendarDayButtonStyle " +
                    $"(Style: {button.Style?.TargetType.Name ?? "null"})");
                var bd = button.Template.FindName("Bd", button) as Border;
                Assert.True(bd != null,
                    "В шаблоне клетки дня нет рамки «Bd» — применён дефолт Aero2");
                Assert.Equal(pill, bd!.CornerRadius);
            }

            // (2) Подписи дней недели: ровно 7 и приглушены — значит найден
            // компонентный ключ DayTitleTemplate (иначе унаследован TextPrimary).
            var monthView = (Grid)FindNamed(calendar, "PART_MonthView")!;
            var dayTitles = monthView.Children
                .Cast<UIElement>()
                .Where(e => Grid.GetRow(e) == 0)
                .ToList();
            Assert.True(dayTitles.Count == 7,
                $"Подписей дней недели: {dayTitles.Count} (ожидалось 7)");

            var muted = (Brush)app.Resources["TextMuted"];
            foreach (var title in dayTitles)
            {
                // Строку дней недели CalendarItem строит ИЗ шаблона
                // DayTitleTemplate, поэтому в сетке лежит уже сам TextBlock.
                var text = title as TextBlock ?? FindDescendants<TextBlock>(title).FirstOrDefault();
                Assert.True(text != null,
                    $"В подписи дня недели нет TextBlock: {title.GetType().Name}");
                Assert.True(BrushEquals(text!.Foreground, muted),
                    $"Подпись дня не приглушена: {text.Foreground}");
            }

            // (3) Selected-день: акцентная заливка и текст «на акценте».
            var selected = dayButtons.First(b => b.IsSelected);
            var selectedBd = (Border)selected.Template.FindName("Bd", selected)!;
            Assert.True(BrushEquals(selectedBd.Background, (Brush)app.Resources["Accent"]),
                $"Selected-день не акцентный: {selectedBd.Background}");
            Assert.True(BrushEquals(selected.Foreground, (Brush)app.Resources["OnAccent"]),
                $"Текст selected-дня не «на акценте»: {selected.Foreground}");

            // (4) Шрифт календаря — вшитый Inter.
            Assert.True((calendar.FontFamily?.Source ?? "").Contains("Inter", StringComparison.OrdinalIgnoreCase),
                $"Шрифт календаря: {calendar.FontFamily?.Source} (ожидался вшитый Inter)");

            // (5) Режим года: 12 клеток месяца/года тоже под стилем.
            calendar.DisplayMode = CalendarMode.Year;
            Layout(calendar);
            var monthCells = FindDescendants<CalendarButton>(calendar).ToList();
            Assert.True(monthCells.Count == 12,
                $"Клеток года: {monthCells.Count} (ожидалось 12)");
            Assert.True(calendar.CalendarButtonStyle != null,
                "В стиле Calendar не задан CalendarButtonStyle");
            foreach (var cell in monthCells)
            {
                Assert.True(ReferenceEquals(cell.Style, calendar.CalendarButtonStyle),
                    $"Клетка года «{cell.Content}» не получила CalendarButtonStyle");
                var bd = cell.Template.FindName("Bd", cell) as Border;
                Assert.True(bd != null,
                    "В шаблоне клетки года нет рамки «Bd» — применён дефолт Aero2");
            }

            // (6) Хром попапа рисует шаблон Calendar: поверхность, рамка,
            // скругление Radius.Md (внутри попапа Calendar нельзя обернуть
            // в свой Border — фреймворк заменит контейнер вместе со стилем).
            Assert.True(BrushEquals(calendar.Background, (Brush)app.Resources["Surface"]),
                $"Фон календаря не Surface: {calendar.Background}");
            Assert.True(BrushEquals(calendar.BorderBrush, (Brush)app.Resources["Border"]),
                $"Рамка календаря не Border: {calendar.BorderBrush}");
            Assert.Equal(1, calendar.BorderThickness.Left);
            var chrome = (Border)calendar.Template.FindName("Chrome", calendar)!;
            Assert.Equal((CornerRadius)app.Resources["Radius.Md"], chrome.CornerRadius);
        }

        private static void Layout(FrameworkElement element)
        {
            element.Measure(new Size(300, 300));
            element.Arrange(new Rect(0, 0, 300, 300));
            element.UpdateLayout();
        }

        /// <summary>Сравнение кистей по цвету: DynamicResource может вернуть
        /// другой экземпляр той же кисти после переключения темы.</summary>
        private static bool BrushEquals(Brush? a, Brush? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is SolidColorBrush sa && b is SolidColorBrush sb) return sa.Color.Equals(sb.Color);
            return false;
        }
    }
}
