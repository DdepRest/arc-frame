using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Stage-3 hardening (Фаза D): pre-fill helpers for
    /// <see cref="AiClarificationForm"/> extracted to keep the form's
    /// state/INPC/TryBuildCommand/BuildSummaryText in a single ~400-line
    /// file. The prefillers are pure state mutations — given a form
    /// instance and a source (user text / reply text / parsed command)
    /// they fill the fields the source already names so the user only
    /// has to pick what's genuinely still unknown.
    /// </summary>
    internal static class AiClarificationPrefill
    {
        /// <summary>
        /// Fills the form with the values the user named in
        /// <paramref name="request"/>: product, color, Anwis mode,
        /// installation, dimensions, quantity. The order of regex checks
        /// («4 шт 739х1116» wins over a bare leading number) is preserved
        /// verbatim from the previous inline implementation.
        /// </summary>
        internal static void FromRequest(AiClarificationForm form, string? request)
        {
            if (string.IsNullOrWhiteSpace(request)) return;
            var t = request!.ToLowerInvariant();

            var color = AiKeywordLexicon.DetectColor(t);
            if (color != null && form.Colors.Contains(color))
                form.SelectedColor = color;

            var mode = AiKeywordLexicon.DetectAnwisMode(t);
            if (mode != null && form.IsAnwis)
                form.SelectedAnwisMode = mode;

            // Pre-fill the installation choice the user already named
            // («с монтажом»/«без монтажа»/«в конструкцию») so the card only
            // asks for what's genuinely still unknown.
            var installation = AiKeywordLexicon.DetectInstallationMode(request);
            if (installation >= 0)
                form.SelectedInstallation = AiKeywordLexicon.InstallationLabel(installation);

            // Every size the request names becomes a row of its own: one screenshot
            // (or one line) regularly carries several positions — «ПМС Anwis, бел.
            // 1 304x1593» and «ПМС Anwis, бел. 1 724x1593» — and all of them must
            // reach the order. Parsing per size also keeps a reliable quantity
            // («4 шт», «4 739х1116») attached to the size it belongs to.
            var entries = AiKeywordLexicon.ParseSizeEntries(request);
            var applicableEntries = form.IsAnwis || form.SelectedType != "Свой товар"
                ? entries.Where(e => !e.IsGlassUnit).ToList()
                : entries;
            ApplySizeEntries(form, applicableEntries);
            if (applicableEntries.Count == 0 && entries.Count == 0)
            {
                // No readable pair (OCR glued the digits: «3711217»): the
                // dimensions stay empty for manual entry, but a quantity that IS
                // readable («2 шт») must not be thrown away with them.
                var quantity = AiKeywordLexicon.QuantityRegex.Match(request);
                if (quantity.Success)
                    form.QuantityText = quantity.Groups[1].Value;
            }
        }

        /// <summary>
        /// Writes sizes read from one source into the card's rows, growing the
        /// list when the source names more positions than the card already holds.
        /// Sources are applied in priority order (parsed commands → model reply
        /// → the user's own words), so a later source reshapes the SAME rows and
        /// the user always wins.
        /// </summary>
        internal static void ApplySizeEntries(
            AiClarificationForm form, IReadOnlyList<AiKeywordLexicon.AiSizeEntry> entries)
        {
            if (entries.Count == 0) return;
            form.EnsureSizeRows(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                var row = form.SizeRows[i];
                row.WidthText = entries[i].Width.ToString(CultureInfo.InvariantCulture);
                row.HeightText = entries[i].Height.ToString(CultureInfo.InvariantCulture);
                row.QuantityText = FormatQuantity(entries[i].Quantity);
            }
        }

        /// <summary>
        /// Pre-fills the size rows from the AddItem parameters the model already
        /// produced. One row per parsed position, so a model that DID understand
        /// the two-position screenshot is not flattened back to a single item by
        /// the card. The Anwis size mode is deliberately NOT copied: the card
        /// exists precisely because that mode must be re-picked by the user.
        /// </summary>
        internal static void FromCommands(AiClarificationForm form, IReadOnlyList<AiCommandParams>? commands)
        {
            if (commands is not { Count: > 0 }) return;

            var first = commands[0];
            if (first != null)
            {
                if (!string.IsNullOrWhiteSpace(first.Type) && form.ProductTypes.Contains(first.Type))
                    form.SelectedType = first.Type;
                if (!string.IsNullOrWhiteSpace(first.Color) && form.Colors.Contains(first.Color))
                    form.SelectedColor = first.Color;
            }

            form.EnsureSizeRows(commands.Count);
            for (int i = 0; i < commands.Count; i++)
            {
                var p = commands[i];
                if (p == null) continue;
                var row = form.SizeRows[i];
                if (p.Width > 0) row.WidthText = p.Width.ToString(CultureInfo.InvariantCulture);
                if (p.Height > 0) row.HeightText = p.Height.ToString(CultureInfo.InvariantCulture);
                if (p.Quantity > 0) row.QuantityText = FormatQuantity(p.Quantity);
            }
        }

        /// <summary>
        /// Pre-fills from the model's prose reply. Vision models often
        /// read parameters off an attached photo and answer with plain
        /// text («Вижу 700×1400, белый. Какой режим?») instead of a
        /// structured <c>add_item</c>; this catches those. The Anwis
        /// mode is deliberately NOT picked here — the reply's
        /// «ББ60, ББ70, ПП…» is an options list, not a user choice.
        /// </summary>
        internal static void FromReply(AiClarificationForm form, string? reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return;
            var t = reply!.ToLowerInvariant();

            // Narrow the selected product:
            // 1) Explicit mention of Anwis / анвис:
            if ((t.Contains("anwis", StringComparison.Ordinal) || t.Contains("анвис", StringComparison.Ordinal))
                && form.ProductTypes.Contains("Anwis"))
            {
                form.SelectedType = "Anwis";
            }
            else
            {
                // 2) Narrow when the reply names exactly one family (e.g. «Отлив»).
                var family = AiClarificationForm.FilterProductsForRequest(reply);
                if (family.Count == 1 && form.ProductTypes.Contains(family[0]))
                    form.SelectedType = family[0];
            }

            var color = AiKeywordLexicon.DetectColor(t);
            if (color != null && form.Colors.Contains(color))
                form.SelectedColor = color;

            // A reply that lists several confirmed pairs («371×1217 и 619×1295»)
            // fills several rows — the model may well have read the whole picture
            // even when the local OCR saw only part of it.
            // Glass unit dimensions («СП 615×1303») must NEVER be prefilled
            // into mosquito net forms!
            var entries = AiKeywordLexicon.ParseSizeEntries(reply);
            var applicableEntries = form.IsAnwis || form.SelectedType != "Свой товар"
                ? entries.Where(e => !e.IsGlassUnit).ToList()
                : entries;
            ApplySizeEntries(form, applicableEntries);
            if (applicableEntries.Count == 0 && entries.Count == 0)
            {
                // Keep quantity parsing independent from dimensions: a vision
                // reply may reliably state «1 шт» even when it omitted or
                // damaged the size. Don't inherit quantity if all entries
                // were glass units.
                var quantity = AiKeywordLexicon.QuantityRegex.Match(reply);
                if (quantity.Success)
                    form.QuantityText = quantity.Groups[1].Value;
            }
        }

        /// <summary>
        /// Pre-fills from an already-parsed <see cref="AiCommandParams"/>.
        /// The model can recover size/color/quantity from earlier context
        /// or an attachment even when the raw user text doesn't contain
        /// them — without this the card would come up blank despite the
        /// program already knowing the values. The Anwis size mode is
        /// deliberately NOT copied: the card exists precisely because
        /// that mode was guessed and must be re-picked by the user.
        /// </summary>
        internal static void FromCommand(AiClarificationForm form, AiCommandParams? p)
        {
            if (p == null) return;

            if (!string.IsNullOrWhiteSpace(p.Type) && form.ProductTypes.Contains(p.Type))
                form.SelectedType = p.Type;

            if (!string.IsNullOrWhiteSpace(p.Color) && form.Colors.Contains(p.Color))
                form.SelectedColor = p.Color;

            if (p.Width > 0)
                form.WidthText = p.Width.ToString(CultureInfo.InvariantCulture);
            if (p.Height > 0)
                form.HeightText = p.Height.ToString(CultureInfo.InvariantCulture);

            if (p.Quantity > 0)
                form.QuantityText = FormatQuantity(p.Quantity);
        }

        private static string FormatQuantity(double q)
            => q == System.Math.Floor(q)
                ? ((int)q).ToString(CultureInfo.InvariantCulture)
                : q.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
