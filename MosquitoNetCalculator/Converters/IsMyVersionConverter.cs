using System;
using System.Globalization;
using System.Windows.Data;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// True, когда версия записи журнала обновлений совпадает с версией
    /// запущенного приложения — бейдж «Ваша версия» в «Истории обновлений».
    ///
    /// Сравнение лояльное к формату: «3.49» считается равной «3.49.0»
    /// (System.Version.CompareTo, а не Equals). Версия 0.0.0 (сбой резолва
    /// в single-file) никогда не совпадает. Некорректные строки — false.
    /// </summary>
    public class IsMyVersionConverter : IValueConverter
    {
        private readonly Version? _override;

        public IsMyVersionConverter() { }

        /// <summary>Тестовый конструктор: подставляет версию вместо
        /// <see cref="UpdateService.CurrentVersion"/>.</summary>
        public IsMyVersionConverter(Version? currentOverride) => _override = currentOverride;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string versionString)
                return false;

            var itemVersion = VersionResolver.ParseSafe(VersionResolver.StripVersionSuffix(versionString));
            var current = _override ?? UpdateService.CurrentVersion;

            if (itemVersion == null || current == null)
                return false;
            // 0.0.0 — резолв версии не удался; не помечаем никакую запись.
            if (current.Major == 0 && current.Minor == 0 && current.Build == 0)
                return false;

            // Лояльное сравнение: «3.49» == «3.49.0». Прямой CompareTo здесь
            // ловит Version-ловушку (как в GOTCHAS про BrokenVersion):
            // Version(3,49).CompareTo(Version(3,49,0)) != 0, т.к. Build = -1.
            return Normalize(itemVersion).CompareTo(Normalize(current)) == 0;
        }

        private static Version Normalize(Version v) =>
            new(v.Major, v.Minor, Math.Max(0, v.Build));

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
