using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Converters;
using Xunit;
using MosquitoNetCalculator.Tests.Helpers;

namespace MosquitoNetCalculator.Tests.Converters
{
    /// <summary>
    /// Regression pins for the v3.50.1 prototype-parity uppercase header template:
    /// caption rendering must never mutate the Header string itself, because
    /// MainWindow's BeginningEdit handler compares e.Column.Header against exact
    /// column names ("Ширина"/"Высота") to lock cells — business logic.
    /// WPF objects need an STA thread — same pattern as QuickAddControlTests.
    /// </summary>
    public class UppercaseHeaderTemplateTests
    {
        private static void RunOnStaThread(Action action)
        {
            // Единая STA-нить процесса (WpfTestHelper): ресурсы WPF
            // создаются и применяются на одном диспетчере — без кросс-поточных
            // VerifyAccess-падений. 10 с — прежний таймаут.
            WpfTestHelper.RunOnSta(action, 10_000);
        }

        [Fact]
        public void Apply_CoversEveryColumn_AndLeavesHeaderStringsUntouched()
        {
            RunOnStaThread(() =>
            {
                var grid = new DataGrid();
                grid.Columns.Add(new DataGridTextColumn { Header = "Ширина" });
                grid.Columns.Add(new DataGridTextColumn { Header = "Высота" });
                grid.Columns.Add(new DataGridTemplateColumn { Header = "" });

                UppercaseHeaderTemplate.Apply(grid);

                Assert.All(grid.Columns, c => Assert.Same(UppercaseHeaderTemplate.Template, c.HeaderTemplate));
                Assert.Equal(
                    new object?[] { "Ширина", "Высота", "" },
                    grid.Columns.Select(c => c.Header));
            });
        }

        [Fact]
        public void UppercaseConverter_Convert_RussianCaption()
        {
            var c = new UppercaseConverter();
            Assert.Equal("НАИМЕНОВАНИЕ", c.Convert("Наименование", typeof(string), null!, null!));
            Assert.Equal(string.Empty, c.Convert(null!, typeof(string), null!, null!));
        }
    }
}
