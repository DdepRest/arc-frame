using System.Globalization;
using System.Windows;
using MosquitoNetCalculator.Converters;
using Xunit;

namespace MosquitoNetCalculator.Tests.Converters
{
    public class ChangePartConverterTests
    {
        private readonly ChangePartConverter _converter = new();

        [Fact]
        public void SplitChange_StandardFormat_ExtractsTopicAndCapitalizedBody()
        {
            string input = "Витрина шаблонов: убрана пустая тёмная область внизу окна.";
            var (topic, body) = ChangePartConverter.SplitChange(input);

            Assert.Equal("Витрина шаблонов", topic);
            Assert.Equal("Убрана пустая тёмная область внизу окна.", body);
        }

        [Fact]
        public void SplitChange_NoColon_ReturnsEmptyTopicAndFullBody()
        {
            string input = "Мелкие исправления и улучшения стабильности работы программы.";
            var (topic, body) = ChangePartConverter.SplitChange(input);

            Assert.Equal(string.Empty, topic);
            Assert.Equal(input, body);
        }

        [Fact]
        public void SplitChange_NullOrWhitespace_ReturnsEmptyStrings()
        {
            var (topic1, body1) = ChangePartConverter.SplitChange(null);
            Assert.Equal(string.Empty, topic1);
            Assert.Equal(string.Empty, body1);

            var (topic2, body2) = ChangePartConverter.SplitChange("   ");
            Assert.Equal(string.Empty, topic2);
            Assert.Equal(string.Empty, body2);
        }

        [Fact]
        public void Convert_Parameters_WorkCorrectly()
        {
            string input = "Печать КП: чистый экземпляр для заказчика теперь первый.";

            var topic = _converter.Convert(input, typeof(string), "topic", CultureInfo.InvariantCulture);
            var body = _converter.Convert(input, typeof(string), "body", CultureInfo.InvariantCulture);
            var hasTopic = _converter.Convert(input, typeof(Visibility), "hasTopic", CultureInfo.InvariantCulture);

            Assert.Equal("Печать КП", topic);
            Assert.Equal("Чистый экземпляр для заказчика теперь первый.", body);
            Assert.Equal(Visibility.Visible, hasTopic);
        }

        [Fact]
        public void Convert_NoTopic_HasTopicReturnsCollapsed()
        {
            string input = "Обновлены внутренние зависимости приложения.";

            var topic = _converter.Convert(input, typeof(string), "topic", CultureInfo.InvariantCulture);
            var body = _converter.Convert(input, typeof(string), "body", CultureInfo.InvariantCulture);
            var hasTopic = _converter.Convert(input, typeof(Visibility), "hasTopic", CultureInfo.InvariantCulture);

            Assert.Equal(string.Empty, topic);
            Assert.Equal(input, body);
            Assert.Equal(Visibility.Collapsed, hasTopic);
        }
    }
}
