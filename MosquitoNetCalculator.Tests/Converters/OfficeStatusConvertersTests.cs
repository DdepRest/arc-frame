using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MosquitoNetCalculator.Converters;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Converters
{
    public class OfficeStatusConvertersTests
    {
        [Fact]
        public void BadgeBackgroundConverter_NoData_ReturnsNeutralChipBg()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var conv = new OfficeStatusToBadgeBackgroundConverter();
                var result = conv.Convert(OfficeStatus.NoData, typeof(Brush), null!, CultureInfo.InvariantCulture) as Brush;
                var expected = Application.Current.Resources["ChipBg"] as Brush;
                var danger = Application.Current.Resources["BadgeDangerBg"] as Brush;

                Assert.NotNull(result);
                Assert.Same(expected, result);
                Assert.NotSame(danger, result);
            });
        }

        [Fact]
        public void BadgeForegroundConverter_NoData_ReturnsTextSecondary()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var conv = new OfficeStatusToBadgeForegroundConverter();
                var result = conv.Convert(OfficeStatus.NoData, typeof(Brush), null!, CultureInfo.InvariantCulture) as Brush;
                var expected = Application.Current.Resources["TextSecondary"] as Brush;
                var danger = Application.Current.Resources["BadgeDangerFg"] as Brush;

                Assert.NotNull(result);
                Assert.Same(expected, result);
                Assert.NotSame(danger, result);
            });
        }

        [Theory]
        [InlineData(OfficeStatus.UpToDate, "BadgeSuccessBg", "BadgeSuccessFg")]
        [InlineData(OfficeStatus.Outdated, "BadgeWarningBg", "BadgeWarningFg")]
        [InlineData(OfficeStatus.NoData, "ChipBg", "TextSecondary")]
        public void Converters_MapExpectedThemeKeys(OfficeStatus status, string expectedBgKey, string expectedFgKey)
        {
            TestAppThemes.RunOnSta(() =>
            {
                var bgConv = new OfficeStatusToBadgeBackgroundConverter();
                var fgConv = new OfficeStatusToBadgeForegroundConverter();

                var bg = bgConv.Convert(status, typeof(Brush), null!, CultureInfo.InvariantCulture) as Brush;
                var fg = fgConv.Convert(status, typeof(Brush), null!, CultureInfo.InvariantCulture) as Brush;

                Assert.Same(Application.Current.Resources[expectedBgKey] as Brush, bg);
                Assert.Same(Application.Current.Resources[expectedFgKey] as Brush, fg);
            });
        }
    }
}
