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
                    "0" or "монтаж включён" or "монтаж включен" or "включён" or "включен" or "с монтажом" or "с монтажём" or "монтаж" or "монтаж вкл" or "монтаж вкл." or "вкл монтаж" => 0,
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
            if (ContainsAny(t, "с монтажом", "с монтажём", "монтаж включён", "монтаж включен", "монтаж вкл", "монтаж вкл.", "вкл монтаж", "монтаж включ")) return 0;
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

        /// <summary>
        /// Replaces a compact digit run («3711217») with an explicit pair
        /// («371×1217») ONLY when the digit sequences match exactly — i.e. the
        /// pair came from a trustworthy independent source (a second OCR engine,
        /// a file name, the model's reply, or a parsed <c>add_item</c>). Never
        /// splits a run when the pair is unknown: «400×1500» and «1500×400» are
        /// both plausible and guessing an orientation would be wrong.
        /// </summary>
        public static string NormalizeCompactDimension(string? text, string? width, string? height)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
            var w = new string((width ?? string.Empty).Where(char.IsDigit).ToArray());
            var h = new string((height ?? string.Empty).Where(char.IsDigit).ToArray());
            if (w.Length == 0 || h.Length == 0) return text;

            var pattern = $"(?<!\\d){Regex.Escape(w)}\\s*{Regex.Escape(h)}(?!\\d)";
            return Regex.Replace(text, pattern, $"{w}×{h}", RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// True when the OCR text contains a long digit run without any explicit
        /// dimension separator — i.e. the engine likely glued width and height
        /// together («3711217»). Such text must NOT be shown in the chat bubble:
        /// splitting it would be a guess. Once the pair is confirmed, the bubble
        /// is filled with the readable form.
        /// </summary>
        public static bool ShouldHideOcrFromBubble(string? text)
            => !string.IsNullOrWhiteSpace(text)
               && Regex.IsMatch(text, @"\d{6,}", RegexOptions.CultureInvariant)
               && !DimensionRegex.IsMatch(text);

        /// <summary>A standalone number right before the size («4 739х1116» → 4).</summary>
        public static readonly Regex LeadingNumberRegex = new(
            @"(\d+(?:[.,]\d+)?)\s*$",
            RegexOptions.CultureInvariant);

        // ── Multiple positions in one request ─────────────────────────

        /// <summary>
        /// One size read off a request / OCR / file-name line: the pair in mm
        /// plus the quantity written right before it (1 when the source names
        /// no quantity). When <see cref="IsGlassUnit"/> is true, the size is
        /// explicitly associated with an insulated glass unit («СП» / «стеклопакет»)
        /// and must not be pre-filled into mosquito net cards.
        /// </summary>
        public readonly record struct AiSizeEntry(int Width, int Height, double Quantity, bool IsGlassUnit = false);

        private static readonly Regex GlassUnitRegex = new(
            @"(?:(?<=\W|^)(?:сп|с/п|с\.п\.|стеклопакет\w*)(?=\W|$))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Returns true if the text immediately preceding or following a dimension match
        /// designates it as an insulated glass unit («СП», «с/п», «стеклопакет»).
        /// </summary>
        internal static bool IsGlassUnitMention(string before, string after)
        {
            var bClause = before;
            int lastB = bClause.LastIndexOfAny(new[] { '.', '?', '!', '\n', ';', ',' });
            if (lastB >= 0)
                bClause = bClause.Substring(lastB + 1);

            if (GlassUnitRegex.IsMatch(bClause))
                return true;

            var aClause = after;
            int firstA = aClause.IndexOfAny(new[] { '.', '?', '!', '\n', ';', ',' });
            if (firstA >= 0)
                aClause = aClause.Substring(0, firstA);

            return GlassUnitRegex.IsMatch(aClause);
        }

        /// <summary>
        /// Every size named in <paramref name="text"/>, in the order it appears.
        /// A manager often sends one picture (or one line) with SEVERAL
        /// positions — «ПМС Anwis, бел. 1 304x1593» and «ПМС Anwis, бел. 1
        /// 724x1593» — and the app must add all of them, not just the first.
        ///
        /// The quantity is taken from the text BETWEEN two sizes («2 шт
        /// 739х1116», «1 304x1593»), so a count belongs to the size that
        /// follows it. Identical pairs are de-duplicated (first occurrence
        /// wins): the same order is regularly present twice in one request —
        /// once in the attachment file name («ПМС Anwis, бел. 1 619x1295.png»)
        /// and once in the OCR of its pixels — and adding the position twice
        /// would inflate the order. A repeated size the manager really wants
        /// twice is entered as quantity in the card.
        /// </summary>
        public static IReadOnlyList<AiSizeEntry> ParseSizeEntries(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<AiSizeEntry>();
            var matches = DimensionRegex.Matches(text!);
            if (matches.Count == 0) return Array.Empty<AiSizeEntry>();

            var result = new List<AiSizeEntry>(matches.Count);
            int previousEnd = 0;
            foreach (Match m in matches)
            {
                if (!int.TryParse(m.Groups[1].Value, out int width)
                    || !int.TryParse(m.Groups[2].Value, out int height)
                    || width <= 0 || height <= 0)
                    continue;

                var quantity = DetectQuantity(text!, previousEnd, m.Index, m.Index + m.Length);
                var before = m.Index > previousEnd ? text!.Substring(previousEnd, m.Index - previousEnd) : string.Empty;
                var after = m.Index + m.Length < text!.Length ? text!.Substring(m.Index + m.Length) : string.Empty;
                bool isGlass = IsGlassUnitMention(before, after);
                previousEnd = m.Index + m.Length;

                if (result.Any(e => e.Width == width && e.Height == height)) continue;
                result.Add(new AiSizeEntry(width, height, quantity, isGlass));
            }
            return result;
        }

        /// <summary>
        /// Quantity that belongs to the size spanning <paramref name="sizeStart"/>..
        /// <paramref name="sizeEnd"/>: «4 шт 739х1116» and «4 739х1116» put the
        /// count in front, «739х1116 4 шт» right behind it. Supports Russian number
        /// words: «для двух сеток 1272х608», «две сетки 700х1400». The rest of the text
        /// between two sizes belongs to neither (a count on another line —
        /// «сетка 700х1400» then «герметик 2 шт» — must not leak onto the size).
        /// Only a number standing on its own counts, and anything outside 1..99
        /// is ignored: a date or an order number in the same line must never
        /// become «шт.».
        /// </summary>
        private static double DetectQuantity(string text, int previousEnd, int sizeStart, int sizeEnd)
        {
            var before = sizeStart > previousEnd ? text.Substring(previousEnd, sizeStart - previousEnd) : string.Empty;
            if (TryReadQuantity(before, out var quantity)) return quantity;

            // «739х1116 4 шт» — only the tail up to the next line break, so a
            // count written on a following line stays with its own position.
            var after = sizeEnd < text.Length ? text.Substring(sizeEnd) : string.Empty;
            int lineBreak = after.IndexOf('\n');
            if (lineBreak >= 0) after = after.Substring(0, lineBreak);
            if (TryReadQuantity(after, out quantity)) return quantity;

            return 1;
        }

        private static bool TryReadQuantity(string segment, out double value)
        {
            // 1. Text number words: «для двух сеток», «две сетки», «три штуки»
            var wordMatch = Regex.Match(
                segment,
                @"(?:\bдля\s+)?\b(дв[уеао][хмя]?|два|две|двух|двумя|тр[её]х|три|тремя|четыр[её]х|четыре|четырьмя|пят[ьи]|пять|шест[ьи]|шесть|сем[ьи]|семь|восем[ьи]|восемь|девят[ьи]|девять|десят[ьи]|десять)\b(?:\s*(?:сето?к\w*|шту?к\w*|позици\w*|издели\w*))?",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (wordMatch.Success)
            {
                var stem = wordMatch.Groups[1].Value.ToLowerInvariant();
                double parsedWord = stem switch
                {
                    "два" or "две" or "двух" or "двумя" => 2,
                    "три" or "трех" or "трёх" or "тремя" => 3,
                    "четыре" or "четырех" or "четырёх" or "четырьмя" => 4,
                    "пять" or "пяти" => 5,
                    "шесть" or "шести" => 6,
                    "семь" or "семи" => 7,
                    "восемь" or "восьми" => 8,
                    "девять" or "девяти" => 9,
                    "десять" or "десяти" => 10,
                    _ => 0
                };
                if (parsedWord > 0)
                {
                    value = parsedWord;
                    return true;
                }
            }

            var withUnit = QuantityRegex.Match(segment);
            if (withUnit.Success && IsPlausibleQuantity(withUnit.Groups[1].Value, allowFraction: true, out value))
                return true;

            var trailing = LeadingNumberRegex.Match(segment.TrimEnd());
            if (trailing.Success && IsPlausibleQuantity(trailing.Groups[1].Value, allowFraction: false, out value))
            {
                // The number must stand on its own (start of the segment or after
                // a space) — digits glued into a longer token are never a count.
                int at = segment.TrimEnd().Length - trailing.Length;
                if (at == 0 || char.IsWhiteSpace(segment[at - 1]))
                    return true;
            }

            value = 1;
            return false;
        }

        private static bool IsPlausibleQuantity(string raw, bool allowFraction, out double value)
        {
            value = 1;
            if (!MoneyFormatService.TryParse(raw, out var parsed) || parsed <= 0 || parsed > 99) return false;
            if (!allowFraction && parsed != Math.Floor(parsed)) return false;
            value = parsed;
            return true;
        }

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>True when <paramref name="text"/> contains any of the <paramref name="keywords"/>.</summary>
        public static bool ContainsAny(string text, params string[] keywords)
            => keywords.Any(k => text.Contains(k, StringComparison.Ordinal));
    }
}
