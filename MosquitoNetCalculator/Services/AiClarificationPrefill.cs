using System;
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

            // Parse each field independently. OCR can lose a dimension separator,
            // but that must not discard a reliable quantity («1»/«1 шт») or other
            // fields already present in the same text.
            var dim = AiKeywordLexicon.DimensionRegex.Match(request);
            if (dim.Success)
            {
                form.WidthText = dim.Groups[1].Value;
                form.HeightText = dim.Groups[2].Value;
            }

            // «4 шт 739х1116» wins over a bare leading number; otherwise a
            // number right before the size («4 739х1116») is treated as count.
            var q = AiKeywordLexicon.QuantityRegex.Match(request);
            if (q.Success)
            {
                form.QuantityText = q.Groups[1].Value;
            }
            else if (dim.Success)
            {
                var beforeSize = request.Substring(0, dim.Index);
                var leading = AiKeywordLexicon.LeadingNumberRegex.Match(beforeSize);
                if (leading.Success)
                    form.QuantityText = leading.Groups[1].Value;
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

            // Narrow the selected product only when the reply names exactly one
            // family (e.g. «Отлив»). Mesh requests resolve to several products,
            // so the default selection stays untouched.
            var family = AiClarificationForm.FilterProductsForRequest(reply);
            if (family.Count == 1 && form.ProductTypes.Contains(family[0]))
                form.SelectedType = family[0];

            var color = AiKeywordLexicon.DetectColor(t);
            if (color != null && form.Colors.Contains(color))
                form.SelectedColor = color;

            var dim = AiKeywordLexicon.DimensionRegex.Match(reply);
            if (dim.Success)
            {
                form.WidthText = dim.Groups[1].Value;
                form.HeightText = dim.Groups[2].Value;
            }

            // Keep quantity parsing independent from dimensions: a vision reply
            // may reliably state «1 шт» even when it omitted or damaged the size.
            var q = AiKeywordLexicon.QuantityRegex.Match(reply);
            if (q.Success)
                form.QuantityText = q.Groups[1].Value;
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
