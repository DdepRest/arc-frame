using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// v3.50.1 (prototype parity): renders DataGrid column captions in upper case,
    /// matching the prototype's uppercase 10.5px column headers.
    ///
    /// Assigned from code (not XAML) because a template with
    /// {Binding Converter={DynamicResource ...}} crashes at parse time —
    /// Converter is not a DependencyProperty, so DynamicResource cannot target it.
    /// Code assignment needs no resource lookup at all and stays parse-safe in the
    /// raw-XAML test harness.
    ///
    /// The Header strings themselves remain untouched: MainWindow's BeginningEdit
    /// handler compares e.Column.Header against exact column names to lock cells
    /// (business logic preserved).
    /// </summary>
    public static class UppercaseHeaderTemplate
    {
        private static DataTemplate? _template;

        /// <summary>Shared, immutable template — safe to reuse across columns and grids.</summary>
        public static DataTemplate Template => _template ??= Build();

        /// <summary>Applies the uppercase template to every column of the grid.</summary>
        public static void Apply(DataGrid grid)
        {
            foreach (var column in grid.Columns)
            {
                column.HeaderTemplate = Template;
            }
        }

        private static DataTemplate Build()
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new UppercaseConverter() });

            return new DataTemplate { VisualTree = text };
        }
    }
}
