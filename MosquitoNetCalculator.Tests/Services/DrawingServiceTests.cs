using System.Windows.Media;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class DrawingServiceTests
    {
        [Theory]
        [InlineData("Anwis")]
        [InlineData("Дверная сетка")]
        [InlineData("На навесах")]
        [InlineData("Отлив")]
        [InlineData("Козырёк")]
        [InlineData("Короб")]
        [InlineData("ПСУЛ")]
        [InlineData("Откос")]
        [InlineData("Работа")]
        [InlineData("Брус")]
        [InlineData("Пояс")]
        [InlineData("Доставка")]
        [InlineData("Уплотнение")]
        [InlineData("UnknownProduct")]
        public void GetDrawingSvg_ReturnsNonNull_ForKnownAndUnknownNames(string name)
        {
            var svg = DrawingService.GetDrawingSvg(name, 1000, 800);
            Assert.False(string.IsNullOrWhiteSpace(svg));
            Assert.Contains("<svg", svg);
        }

        [Theory]
        [InlineData("Anwis")]
        [InlineData("Дверная сетка")]
        [InlineData("Отлив")]
        [InlineData("Работа")]
        [InlineData("UnknownProduct")]
        public void GetDrawingImage_ReturnsNonNull_ForKnownAndUnknownNames(string name)
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var image = DrawingService.GetDrawingImage(name, 1000, 800);
                Assert.NotNull(image);
            });
        }

        [Fact]
        public void CreateDrawingImageElement_ReturnsImage_WithExpectedProperties()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var img = DrawingService.CreateDrawingImageElement("Anwis", 1000, 800, displayWidth: 42);
                Assert.NotNull(img);
                Assert.Equal(42, img.Width);
                Assert.Equal(System.Windows.Media.Stretch.Uniform, img.Stretch);
                Assert.NotNull(img.Source);
            });
        }

        [Fact]
        public void WrapForCentering_ReturnsGridContainingContent()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var content = new System.Windows.Controls.TextBlock { Text = "test" };
                var wrapped = DrawingService.WrapForCentering(content);
                Assert.IsType<System.Windows.Controls.Grid>(wrapped);
            });
        }
    }
}
