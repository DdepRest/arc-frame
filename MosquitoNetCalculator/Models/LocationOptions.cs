using System.Collections.Generic;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Installation-location option offered on first run / via the "Сменить" button.
    /// Immutable; the static <see cref="LocationOptions.All"/> list is the single
    /// source of truth for both the visible options and their contract prefixes.
    /// </summary>
    public sealed class LocationOption
    {
        public string Prefix { get; }
        public string LocationName { get; }

        public LocationOption(string prefix, string locationName)
        {
            Prefix = prefix;
            LocationName = locationName;
        }

        public override string ToString() => $"{Prefix}: {LocationName}";
    }

    /// <summary>
    /// Canonical list of installation locations. Edit this file to add / rename /
    /// remove a location — do NOT mirror the data into XAML.
    /// </summary>
    public static class LocationOptions
    {
        public const string DefaultPrefix = "1";

        public static IReadOnlyList<LocationOption> All { get; } = new LocationOption[]
        {
            new LocationOption("1", "Красношапки 44 — «Дом Окон+»"),
            new LocationOption("2", "Рудакова 76 — «Компания „Уют”»"),
            new LocationOption("3", "40 Лет Украины 7А/1 — «Дом Окон+»"),
            new LocationOption("4", "Пушкинская 22 — «Студия окон и дверей»"),
            new LocationOption("5", "Жукова 12A - «Дом окон+»"),
        };

        /// <summary>True when <paramref name="prefix"/> matches a known option.</summary>
        public static bool IsValidPrefix(string? prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return false;
            for (int i = 0; i < All.Count; i++)
                if (All[i].Prefix == prefix) return true;
            return false;
        }

        /// <summary>
        /// Returns the matching <see cref="LocationOption"/> for the given prefix,
        /// or <see cref="DefaultPrefix"/>'s option if the prefix is unknown or empty.
        /// Always returns a non-null instance.
        /// </summary>
        public static LocationOption GetByPrefixOrDefault(string? prefix)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Prefix == prefix) return All[i];
            return All[0];
        }
    }
}
