using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Шрифт попапов — контекстное меню и подсказки (v3.54.0).
    ///
    /// <para><b>Что было сломано.</b> Контекстное меню карточек «Заказы»
    /// рисовалось системным <c>Segoe UI</c>, пока весь остальной интерфейс —
    /// вшитым Inter. Стиль <c>MenuItem</c> задавал <c>FontSize</c>, но не
    /// <c>FontFamily</c>, и срабатывало умолчание
    /// <c>Control.FontFamily = SystemFonts.MessageFontFamily</c>. Замер по
    /// кадру пользователя (чернильный бокс строки при 12px):
    /// «Открыть заказ» 78px, «Копировать» 64px — это Segoe UI байт в байт
    /// (Inter даёт 87 и 69).</para>
    ///
    /// <para><b>Почему не наследуется.</b> Меню и подсказки живут в отдельном
    /// Popup-дереве, до которого не доходит ни стиль <c>Window.Shared</c>,
    /// ни семантические стили <c>FontStyles</c>. Обычный <c>Popup</c> с
    /// инлайновым содержимым (меню печати в ActionBar) шрифт получает —
    /// его содержимое остаётся в логическом дереве окна. Разбор — GOTCHAS §29.</para>
    ///
    /// <para><b>Два уровня, как у окна (GOTCHAS §22).</b> Скан разметки ловит
    /// «токен забыли написать»; рантайм-проверка ловит «токен написан, но
    /// элемент всё равно получает системный шрифт» — ровно то, что не видели
    /// тесты окон. Третий кейс — контрольная группа: пункт БЕЗ стиля обязан
    /// остаться на системном шрифте, иначе проверка была бы вакуумной.</para>
    /// </summary>
    [Collection("WPF_UI")]
    public class PopupTypographyTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator", "App.xaml")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Не найден корень репозитория.");
        }

        private static string AppDir => Path.Combine(RepoRoot(), "MosquitoNetCalculator");

        /// <summary>
        /// Типы, содержимое которых WPF хостит в отдельном Popup-дереве:
        /// наследование туда не доходит, поэтому шрифт обязан стоять в стиле.
        /// </summary>
        private static readonly string[] PopupHostedTypes = { "ContextMenu", "MenuItem", "ToolTip" };

        [Fact]
        public void EveryPopupHostedStyle_DeclaresTheFontToken()
        {
            var themeFiles = Directory
                .EnumerateFiles(Path.Combine(AppDir, "Themes"), "*.xaml")
                .ToList();

            var problems = new List<string>();

            foreach (var file in themeFiles)
            {
                string text = Regex.Replace(
                    File.ReadAllText(file), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

                foreach (var type in PopupHostedTypes)
                {
                    foreach (Match m in Regex.Matches(text, $@"<Style\b[^>]*TargetType=""{type}""[^>]*>"))
                    {
                        int end = text.IndexOf("</Style>", m.Index, StringComparison.Ordinal);
                        if (end < 0) continue;

                        string body = text[m.Index..end];
                        if (!body.Contains("{DynamicResource Font.Text}", StringComparison.Ordinal))
                        {
                            problems.Add(
                                $"{Path.GetFileName(file)}: стиль {type} без FontFamily={{DynamicResource Font.Text}} — " +
                                "получит системный Segoe UI (Popup не наследует шрифт окна)");
                        }
                    }
                }
            }

            Assert.True(problems.Count == 0,
                "Стили попапов потеряли токен шрифта:\n  " + string.Join("\n  ", problems));
        }

        [Fact]
        public void PopupStyles_ResolveToTheBundledInter()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var app = Application.Current;

                // Стили берём из ресурсов по типу ровно так, как их находит
                // разметка: OrdersHistoryControl ссылается на них через
                // {StaticResource {x:Type MenuItem}} — нет ключа, нет и стиля.
                var menuStyle = app.Resources[typeof(ContextMenu)] as Style;
                var itemStyle = app.Resources[typeof(MenuItem)] as Style;
                var tipStyle = app.Resources[typeof(ToolTip)] as Style;

                Assert.NotNull(menuStyle);
                Assert.NotNull(itemStyle);
                Assert.NotNull(tipStyle);

                var problems = new List<string>();

                var menu = new ContextMenu { Style = menuStyle };
                var item = new MenuItem { Header = "Открыть заказ", Style = itemStyle };
                menu.Items.Add(item);

                var tip = new ToolTip { Content = "Подсказка", Style = tipStyle };

                void Check(string what, Control control)
                {
                    string family = control.FontFamily?.Source ?? "null";
                    if (!family.Contains("Inter", StringComparison.OrdinalIgnoreCase))
                        problems.Add($"{what}: FontFamily={family} (ожидался вшитый Inter)");

                    double size = control.FontSize;
                    if (Math.Abs(size - 12) > 0.01)
                        problems.Add($"{what}: FontSize={size} (ожидалось 12 = Type.Body)");
                }

                Check("ContextMenu", menu);
                Check("MenuItem", item);
                Check("ToolTip", tip);

                Assert.True(problems.Count == 0,
                    "Попап остался на системном шрифте:\n  " + string.Join("\n  ", problems));
            });
        }

        /// <summary>
        /// Контрольная группа: пункт меню со СНЯТЫМ стилем обязан остаться
        /// на системном шрифте. Стиль нужно снимать явно (<c>Style = null</c>) —
        /// без этого WPF сам подставит неявный стиль по типу, и контроль
        /// ничего не покажет (проверено: пустой <c>new MenuItem()</c> в этом же
        /// приложении тестов уже получает Inter — то есть неявный стиль
        /// применяется и к элементам, созданным кодом).
        ///
        /// <para>Смысл кейса: единственное, что даёт Inter попапу, — сеттер
        /// стиля. Если <see cref="PopupStyles_ResolveToTheBundledInter"/> зеленеет
        /// и после снятия сеттера, она вакуумна.</para>
        /// </summary>
        [Fact]
        public void MenuItem_WithTheStyleRemoved_FallsBackToSystemFont()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var bare = new MenuItem { Header = "Без стиля", Style = null };

                string expected = SystemFonts.MessageFontFamily.Source;
                string actual = bare.FontFamily?.Source ?? "null";

                Assert.Equal(expected, actual);
                Assert.DoesNotContain("Inter", actual, StringComparison.OrdinalIgnoreCase);
            });
        }
    }
}
