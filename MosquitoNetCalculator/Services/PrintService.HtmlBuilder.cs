using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    public partial class PrintService
    {
        private string FillTemplate(
            string template,
            List<OrderItem> items,
            ClientInfo clientInfo,
            double totalAmount,
            string amountInWords)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string drawingSvg = GetDrawingSvg(item.Name, item.Width, item.Height);

                sb.AppendLine(@"<tr>");
                sb.AppendLine($"  <td class='center'>{i + 1}</td>");
                // All string fields from item / clientInfo are HTML-escaped to prevent
                // XSS / markup breakage in the print preview and saved PDF (a malicious
                // or accidental `&`/`<` in the name, color, unit, install label, or
                // contract number would otherwise render literally or break the table).
                sb.AppendLine($"  <td class='name-cell'>{EscapeHtml(item.DisplayName)}</td>");
                sb.AppendLine($"  <td class='center'>{EscapeHtml(item.Color)}</td>");
                sb.AppendLine($"  <td class='center'>{item.Width:F0}</td>");
                sb.AppendLine($"  <td class='center'>{item.Height:F0}</td>");
                sb.AppendLine($"  <td class='center'>{item.Quantity}</td>");
                // Wrap the install mark in a <span class='install-mark'> so the
                // print template's CSS locks the colour to pure black. The
                // title attribute keeps the meaning legible (✓ / ✗ alone is
                // ambiguous in a printed КП). Label comes from OrderItem so
                // wording stays in sync with the grid tooltip.
                sb.AppendLine($"  <td class='center'><span class='install-mark' title='{EscapeHtml(item.InstallationLabel)}'>{EscapeHtml(item.KpInstallationDisplay)}</span></td>");
                sb.AppendLine($"  <td class='center'>{item.CalculatedValue:F3}</td>");
                sb.AppendLine($"  <td class='center'>{EscapeHtml(item.Unit)}</td>");
                sb.AppendLine($"  <td class='right'>{MoneyFormatService.Format(item.Price)}</td>");
                sb.AppendLine($"  <td class='right'>{MoneyFormatService.Format(item.TotalWithDeduction)}</td>");
                sb.AppendLine($"  <td class='drawing'>{drawingSvg}</td>");
                sb.AppendLine(@"</tr>");
            }

            template = template.Replace("{{ROWS}}", sb.ToString());
            var clientBlockSb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(clientInfo.ClientName))
            {
                clientBlockSb.AppendLine($"  <div class='client-row'><span class='client-label'>Заказчик:</span><span class='client-value'>{EscapeHtml(clientInfo.ClientName)}</span></div>");
            }
            if (!string.IsNullOrWhiteSpace(clientInfo.ClientPhone))
            {
                clientBlockSb.AppendLine($"  <div class='client-row'><span class='client-label'>Телефон:</span><span class='client-value'>{EscapeHtml(clientInfo.ClientPhone)}</span></div>");
            }
            if (!string.IsNullOrWhiteSpace(clientInfo.ClientAddress))
            {
                clientBlockSb.AppendLine($"  <div class='client-row'><span class='client-label'>Адрес:</span><span class='client-value'>{EscapeHtml(clientInfo.ClientAddress)}</span></div>");
            }
            // Replace the entire <div class='client-block'>...</div> as one unit so
            // empty client fields are OMITTED (the rows are only appended to
            // clientBlockSb when their value is non-empty). The regex uses \s*
            // between the rows so the match is robust to any line-ending
            // variation (LF/CRLF/CR) in the template resource.
            string clientBlock = clientBlockSb.ToString();
            template = Regex.Replace(
                template,
                @"<div class='client-block'>\s*<div class='client-row'><span class='client-label'>Заказчик:</span><span class='client-value'>{{CLIENT_NAME}}</span></div>\s*<div class='client-row'><span class='client-label'>Телефон:</span><span class='client-value'>{{CLIENT_PHONE}}</span></div>\s*<div class='client-row'><span class='client-label'>Адрес:</span><span class='client-value'>{{CLIENT_ADDRESS}}</span></div>\s*</div>",
                $"<div class='client-block'>{clientBlock}</div>");
            template = template.Replace("{{CONTRACT_NUMBER}}", EscapeHtml(clientInfo.ContractNumber));
            template = template.Replace("{{CONTRACT_DATE}}", clientInfo.ContractDate.ToString("dd.MM.yyyy"));
            template = template.Replace("{{TOTAL_AMOUNT}}", MoneyFormatService.Format(totalAmount));
            template = template.Replace("{{AMOUNT_IN_WORDS}}", EscapeHtml(amountInWords));

            // Additional KP block — iterate over all additional KP entries
            string additionalKpBlock = "";
            var additionalKps = clientInfo.AdditionalKps.Where(kp => kp.IsActive).ToList();
            if (clientInfo.HasAdditionalKp && additionalKps.Count > 0)
            {
                double additionalKpsTotal = additionalKps.Sum(kp => kp.Amount);

                if (additionalKpsTotal > 0)
                {
                    double grandTotal = totalAmount + additionalKpsTotal;
                    string grandTotalWords = MosquitoNetCalculator.Services.AmountInWordsService.Convert(grandTotal);

                    // Build reference lines for each additional KP
                    var detailsSb = new StringBuilder();
                    foreach (var kp in additionalKps)
                    {
                        if (!string.IsNullOrWhiteSpace(kp.Number))
                            detailsSb.AppendLine($"<div class='additional-kp-row'><span>К данному заказу прилагается КП &#8470; {EscapeHtml(kp.Number.Trim())}</span><span class='additional-kp-sum'>{MoneyFormatService.Format(kp.Amount)} руб.</span></div>");
                        else
                            detailsSb.AppendLine($"<div class='additional-kp-row'><span>Дополнительное КП</span><span class='additional-kp-sum'>{MoneyFormatService.Format(kp.Amount)} руб.</span></div>");
                    }

                    // Build total breakdown rows
                    var totalRowsSb = new StringBuilder();
                    totalRowsSb.AppendLine($"<div class='total-row-item'><span class='total-row-label'>Сумма основного КП:</span><span class='total-row-amount'>{MoneyFormatService.Format(totalAmount)} руб.</span></div>");
                    foreach (var kp in additionalKps.Where(kp => kp.Amount > 0))
                    {
                        string label = !string.IsNullOrWhiteSpace(kp.Number)
                            ? $"Сумма доп. КП &#8470; {EscapeHtml(kp.Number.Trim())}"
                            : "Сумма доп. КП";
                        totalRowsSb.AppendLine($"<div class='total-row-item'><span class='total-row-label'>{label}:</span><span class='total-row-amount'>{MoneyFormatService.Format(kp.Amount)} руб.</span></div>");
                    }
                    totalRowsSb.AppendLine($"<div class='total-row-item total-row-grand'><span class='total-row-label'>ОБЩИЙ ИТОГ:</span><span class='total-row-amount'>{MoneyFormatService.Format(grandTotal)} руб.</span></div>");
                    totalRowsSb.AppendLine($"<div class='amount-words'>{EscapeHtml(grandTotalWords)}</div>");

                    string title = additionalKps.Count == 1 ? "Дополнительное КП" : "Дополнительные КП";
                    var blockSb = new StringBuilder();
                    blockSb.AppendLine("<div class='additional-kp'>");
                    blockSb.AppendLine($"  <div class='additional-kp-title'>{title}</div>");
                    blockSb.AppendLine("  <div class='additional-kp-details'>");
                    blockSb.Append(detailsSb);
                    blockSb.AppendLine("  </div>");
                    blockSb.AppendLine("</div>");
                    blockSb.AppendLine("<div class='total-section additional-total'>");
                    blockSb.Append(totalRowsSb);
                    blockSb.AppendLine("</div>");
                    additionalKpBlock = blockSb.ToString();
                }
                else
                {
                    // Has additional KPs but no amounts — just reference text
                    var refSb = new StringBuilder();
                    foreach (var kp in additionalKps.Where(kp => !string.IsNullOrWhiteSpace(kp.Number)))
                    {
                        refSb.AppendLine($"<div class='additional-kp'>К данному заказу прилагается КП &#8470; {EscapeHtml(kp.Number.Trim())}</div>");
                    }
                    additionalKpBlock = refSb.ToString();
                }
            }
            template = template.Replace("{{ADDITIONAL_KP_BLOCK}}", additionalKpBlock);

            // Notes block — only shown if notes are not empty
            string notesBlock = "";
            if (!string.IsNullOrWhiteSpace(clientInfo.Notes))
            {
                notesBlock = $@"<div class='notes-block'><div class='notes-title'>Примечания</div><div>{EscapeHtml(clientInfo.Notes.Trim())}</div></div>";
            }
            template = template.Replace("{{NOTES_BLOCK}}", notesBlock);

            return template;
        }

        /// <summary>
        /// Escapes basic HTML characters to prevent injection
        /// Returns string.Empty for null input.</summary>
        private static string EscapeHtml(string text)
        {
            if (text == null) return string.Empty;
            return text
                .Replace("\r\n", "\n")   // Normalize Windows line endings first
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;")
                .Replace("\n", "<br/>\n");
        }
    }
}
