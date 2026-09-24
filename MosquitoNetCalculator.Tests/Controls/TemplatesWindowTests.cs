using System;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
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
    [Collection("STA")]
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

                    // Пользователь вводит только размеры — валидация проходит
                    // без единого касания выпадающих списков (сценарий бага).
                    state.GridWidth = 500;
                    state.GridHeight = 1500;
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
    }
}
