using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Tests.Helpers;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// PLAYTEST: настоящий <see cref="AiAssistantControl"/> на STA-потоке с темами
    /// App.xaml. Ловит то, чего не видят ни сборка, ни чистые тесты: сетка
    /// «Размеры, мм» — это ItemsControl по строкам размера, у каждой строки своя
    /// кнопка «✕», а «＋ Добавить размер» стоит ВНЕ шаблона. Ошибка в
    /// RelativeSource-биндинге видимости или в обработчике не показала бы кнопку
    /// — молча, без единого отказа.
    ///
    /// <para>Поиск идёт только по ВИДИМЫМ ветвям: карточка каждого сообщения
    /// существует в дереве всегда (свёрнута через <c>HasClarificationForm</c>),
    /// поэтому поиск «по всему дереву» находил бы и кнопки невидимых карточек.</para>
    /// </summary>
    [Collection("WPF_UI")]
    public class AiClarificationCardPlaytestTests
    {
        private static void Flush(FrameworkElement root)
        {
            root.Measure(new Size(root.Width, root.Height));
            root.Arrange(new Rect(0, 0, root.Width, root.Height));
            for (int i = 0; i < 3; i++)
            {
                // ContextIdle ниже DataBind/Render/Loaded: честный барьер «кадр
                // дорисован» — контейнеры ItemsControl материализуются отложенно.
                root.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
                root.UpdateLayout();
            }
        }

        private static AiAssistantViewModel OpenCard(AiClarificationForm form, out AiAssistantControl control)
        {
            TestAppThemes.Ensure();
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage
            {
                Text = "Какой режим Anwis использовать?",
                IsUser = false,
                ClarificationForm = form
            });
            control = new AiAssistantControl { Width = 420, Height = 900, DataContext = vm };
            // Без Measure/Arrange шаблон не применён — визуального дерева нет.
            Flush(control);
            return vm;
        }

        /// <summary>
        /// Все элементы типа <typeparamref name="T"/> из ВИДИМОГО поддерева с
        /// заданным AutomationProperties.Name. Дети свёрнутых (Collapsed) ветвей
        /// не обходятся: они принадлежат чужим сообщениям.
        /// </summary>
        private static List<T> FindVisible<T>(DependencyObject root, string automationName) where T : DependencyObject
        {
            var found = new List<T>();
            if (root is T typed && AutomationProperties.GetName(typed) == automationName) found.Add(typed);
            if (root is UIElement { Visibility: not Visibility.Visible }) return found;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                found.AddRange(FindVisible<T>(VisualTreeHelper.GetChild(root, i), automationName));
            return found;
        }

        private static List<TextBox> SizeBoxes(AiAssistantControl control, string automationName)
            => FindVisible<TextBox>(control, automationName);

        private static List<Button> SizeButtons(AiAssistantControl control, string automationName)
            => FindVisible<Button>(control, automationName);

        [Fact]
        public void Playtest_TwoPositionsFromOneScreenshot_RendersTwoEditableSizeRows()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var form = new AiClarificationForm("ПМС Anwis, бел.\n1 304x1593\nПМС Anwis, бел.\n1 724x1593");
                OpenCard(form, out var control);

                var widths = SizeBoxes(control, "Ширина");
                Assert.Equal(2, widths.Count);
                Assert.Equal(new[] { "304", "724" }, widths.Select(w => w.Text).ToArray());

                var heights = SizeBoxes(control, "Высота");
                Assert.Equal(new[] { "1593", "1593" }, heights.Select(h => h.Text).ToArray());

                // Each row carries its own «✕» — visible while the card holds more
                // than one position (RelativeSource up to the message).
                var remove = SizeButtons(control, "Убрать размер");
                Assert.Equal(2, remove.Count);
                Assert.All(remove, b => Assert.Equal(Visibility.Visible, b.Visibility));

                Assert.Single(SizeButtons(control, "Добавить размер"));
            });
        }

        [Fact]
        public void Playtest_AddAndRemoveSizeRows_WorkFromTheCard()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var form = new AiClarificationForm("ПМС Anwis, бел.\n1 304x1593\nПМС Anwis, бел.\n1 724x1593");
                OpenCard(form, out var control);

                SizeButtons(control, "Добавить размер").Single()
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Flush(control);

                Assert.Equal(3, form.SizeRows.Count);
                Assert.Equal(3, SizeBoxes(control, "Ширина").Count);

                // «✕» второй строки убирает ИМЕННО её (обработчик находит сообщение
                // сквозь дерево: DataContext самой кнопки — строка размера).
                SizeButtons(control, "Убрать размер")[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Flush(control);

                Assert.Equal(2, form.SizeRows.Count);
                Assert.Equal("304", form.SizeRows[0].WidthText);
                Assert.True(form.SizeRows[1].IsEmpty);
            });
        }

        [Fact]
        public void Playtest_CardWithOnePosition_HidesRemoveButton()
        {
            TestAppThemes.RunOnSta(() =>
            {
                // Единственную позицию удалять нечем — иконка скрыта.
                var form = new AiClarificationForm("сетка Anwis 700х1400");
                OpenCard(form, out var control);

                Assert.Single(SizeBoxes(control, "Ширина"));
                var remove = SizeButtons(control, "Убрать размер");
                Assert.NotEmpty(remove);
                Assert.All(remove, b => Assert.Equal(Visibility.Collapsed, b.Visibility));
            });
        }
    }
}
