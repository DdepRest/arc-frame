using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Single source of truth for AI-side keyword / regex detection:
    /// Anwis size modes, installation modes, colors, dimensions and
    /// quantity patterns. Every consumer (parser, validator, plan
    /// builder, clarification form, prompt builder) goes through this
    /// class so the surface used to read the user's text stays
    /// consistent and unit-testable in isolation.
    ///
    /// Previously these regexes and switches lived scattered across
    /// <c>AiCommandParser</c>, <c>AiClarificationForm</c> and
    /// <c>AiAssistantService</c>; the unit tests now live next to the
    /// logic.
    /// </summary>
    public static class AiKeywordLexicon
    {
        // ── Anwis size-mode labels (он же enum → текст) ──────────────

        /// <summary>Russian label for an Anwis enum value, used in product-text.</summary>
        public static string AnwisModeLabel(AnwisSizeMode m) => m switch
        {
            AnwisSizeMode.Брусбокс60 => "ББ60",
            AnwisSizeMode.Брусбокс70 => "ББ70",
            AnwisSizeMode.Профипласт => "ПП",
            AnwisSizeMode.РазмерПроёма => "Проём",
            AnwisSizeMode.Габаритный => "Габарит",
            _ => m.ToString()
        };

        /// <summary>
        /// Parses a Russian/English token into an <see cref="AnwisSizeMode"/>.
        /// Russian and English stems are both accepted; unknown tokens
        /// fall back to the catalog's default mode.
        /// </summary>
        public static AnwisSizeMode ParseAnwisModeString(string s) => s.Trim().ToLowerInvariant() switch
        {
            "бб60" or "bb60" or "брусбокс60" or "брусбокс 60" => AnwisSizeMode.Брусбокс60,
            "бб70" or "bb70" or "брусбокс70" or "брусбокс 70" => AnwisSizeMode.Брусбокс70,
            "пп" or "pp" or "профипласт" => AnwisSizeMode.Профипласт,
            "проём" or "проем" or "размер проёма" => AnwisSizeMode.РазмерПроёма,
            "габарит" or "габаритный" => AnwisSizeMode.Габаритный,
            _ => AnwisSizeService.DefaultMode
        };

        /// <summary>
        /// Inverse: Russian label <c>"ББ 60"</c> used for option lists in
        /// the AI prompt + form. Returns null when no Anwis stem appears in
        /// <paramref name="text"/>.
        /// </summary>
        public static string? DetectAnwisMode(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text!.ToLowerInvariant();
            if (Regex.IsMatch(t, @"\b(бб\s?60|брусбокс\s?60|bb\s?60)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "ББ 60";
            if (Regex.IsMatch(t, @"\b(бб\s?70|брусбокс\s?70|bb\s?70)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "ББ 70";
            if (Regex.IsMatch(t, @"\b(пп|профипласт|pp)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "ПП";
            if (Regex.IsMatch(t, @"\bпро[её]м\w*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Проём";
            if (Regex.IsMatch(t, @"\bгабарит\w*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Габарит";
            return null;
        }

        /// <summary>True when the request explicitly names an Anwis size mode.</summary>
        public static bool AnwisModeSpecified(string? text)
            => !string.IsNullOrWhiteSpace(text) && DetectAnwisMode(text) != null;

        // ── Installation modes ───────────────────────────────────────

        /// <summary>
        /// Parses the JSON value of <c>installation_mode</c> into a 0/1/2
        /// integer (0 = «с монтажом», 1 = «без монтажа», 2 = «в конструкцию»),
        /// accepting both numbers and Russian strings. Null when neither.
        /// </summary>
        public static int? ParseInstallationModeField(JsonElement v)
        {
            if (v.ValueKind == JsonValueKind.Number)
                return v.TryGetInt32(out int n) && n >= 0 && n <= 2 ? n : (int?)null;
            if (v.ValueKind == JsonValueKind.String)
            {
                return v.GetString()?.Trim().ToLowerInvariant() switch
                {
                    "2" or "в конструкцию" or "в конструцию" or "конструкция" => 2,
                    "1" or "без монтажа" or "без установки" or "без" or "не нужно" => 1,
                    "0" or "монтаж включён" or "монтаж включен" or "включён" or "включен" or "с монтажом" or "с монтажём" or "монтаж" => 0,
                    _ => null
                };
            }
            return null;
        }

        /// <summary>
        /// Detects an explicit installation choice in free text. Returns
        /// -1 when the user never mentioned installation, 0/1/2 otherwise.
        /// </summary>
        public static int DetectInstallationMode(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return -1;
            var t = text!.ToLowerInvariant();
            if (ContainsAny(t, "без монтажа", "без монтаж", "без установки")) return 1;
            if (ContainsAny(t, "в конструкцию", "в конструцию", "конструкция")) return 2;
            if (ContainsAny(t, "с монтажом", "с монтажём", "монтаж включён", "монтаж включен")) return 0;
            return -1;
        }

        /// <summary>True when the request mentions an installation choice.</summary>
        public static bool InstallationModeSpecified(string? text)
            => DetectInstallationMode(text) >= 0;

        /// <summary>Russian install label per integer code.</summary>
        public static string InstallationLabel(int? mode) => mode switch
        {
            0 => "С монтажом",
            1 => "Без монтажа",
            2 => "В конструкцию",
            _ => "Не указывать"
        };

        // ── Colors ────────────────────────────────────────────────────

        /// <summary>
        /// Detects a color name in the user's text. Returns null when no
        /// known color stem appears. The sibling list lives in
        /// <see cref="AiFactsProvider.GetColorsFor"/>.
        /// </summary>
        public static string? DetectColor(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text!.ToLowerInvariant();
            if (ContainsAny(t, "бел", "белый", "white")) return "Белый";
            if (ContainsAny(t, "корич", "коричневый", "brown")) return "Коричневый";
            if (t.Contains("антрацит", StringComparison.Ordinal)) return "Антрацит";
            if (ContainsAny(t, "золот", "дуб", "gold")) return "Золотой дуб";
            if (ContainsAny(t, "серый", "серая", "gray", "grey")) return "Серый";
            if (ContainsAny(t, "чёрн", "черн", "черный", "black")) return "Чёрный";
            return null;
        }

        // ── Dimension, quantity, leading-number patterns ──────────────

        /// <summary>«739х1116», «739 x 1116», «739×1116», «739*1116» → width/height.</summary>
        public static readonly Regex DimensionRegex = new(
            @"(\d{2,5})\s*[xх×\*]\s*(\d{2,5})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>«4 шт», «2,5шт» → quantity.</summary>
        public static readonly Regex QuantityRegex = new(
            @"(\d+(?:[.,]\d+)?)\s*шт",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>A standalone number right before the size («4 739х1116» → 4).</summary>
        public static readonly Regex LeadingNumberRegex = new(
            @"(\d+(?:[.,]\d+)?)\s*$",
            RegexOptions.CultureInvariant);

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>True when <paramref name="text"/> contains any of the <paramref name="keywords"/>.</summary>
        public static bool ContainsAny(string text, params string[] keywords)
            => keywords.Any(k => text.Contains(k, StringComparison.Ordinal));
    }
}
