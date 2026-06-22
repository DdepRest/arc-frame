using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    public class PrintService
    {
        /// <summary>
        /// Generates the КП HTML string for preview/printing.
        /// </summary>
        public string GenerateKpHtml(
            List<OrderItem> items,
            ClientInfo clientInfo,
            double totalAmount,
            string amountInWords)
        {
            var validItems = items.Where(i =>
                !string.IsNullOrEmpty(i.Name) &&
                i.Total > 0).ToList();

            if (validItems.Count == 0) return "";

            string template = LoadTemplate();
            return FillTemplate(template, validItems, clientInfo, totalAmount, amountInWords);
        }

        private string LoadTemplate()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "MosquitoNetCalculator.Resources.print_template.html";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }

            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "print_template.html");
            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath, Encoding.UTF8);
            }

            return GetInlineTemplate();
        }

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
                sb.AppendLine($"  <td class='name-cell'>{EscapeHtml(item.Name)}</td>");
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

        /// <summary>
        /// Generates a clean, print-friendly engineering-style SVG drawing for the item.
        /// Grayscale palette only — suitable for both screen and B&W printing.
        /// Products: Anwis, На навесах, Отлив, Козырёк, Короб,
        ///          ПСУЛ, Откос материал, Работа
        /// </summary>
        private static string GetDrawingSvg(string name, double width, double height)
        {
            if (name == "Отлив")
            {
                // Cross-section profile of an ebb/drain
                return $@"<svg width='100' height='36' viewBox='0 0 100 36'>
                    <path d='M10,8 L90,8 L90,14 L70,14 L70,28 L30,28 L30,14 L10,14 Z'
                          fill='#f0f0f0' stroke='#444' stroke-width='1' stroke-linejoin='round'/>
                    <line x1='10' y1='33' x2='90' y2='33' stroke='#555' stroke-width='0.5'/>
                    <line x1='10' y1='30' x2='10' y2='34' stroke='#555' stroke-width='0.5'/>
                    <line x1='90' y1='30' x2='90' y2='34' stroke='#555' stroke-width='0.5'/>
                    <text x='50' y='35' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{width:F0}</text>
                    <line x1='5' y1='8' x2='5' y2='28' stroke='#555' stroke-width='0.5'/>
                    <line x1='3' y1='8' x2='7' y2='8' stroke='#555' stroke-width='0.5'/>
                    <line x1='3' y1='28' x2='7' y2='28' stroke='#555' stroke-width='0.5'/>
                    <text x='4' y='20' font-size='6' text-anchor='middle' fill='#333' font-family='Arial'
                          transform='rotate(-90,4,20)'>{height:F0}</text>
                </svg>";
            }
            else if (name == "Anwis")
            {
                // Anwis system net — rectangle with thin frame + corner clips
                return $@"<svg width='100' height='54' viewBox='0 0 100 54'>
                    <rect x='12' y='4' width='76' height='40' fill='#f5f5f5' stroke='#444' stroke-width='1.2' rx='1'/>
                    <rect x='16' y='8' width='68' height='32' fill='none' stroke='#aaa' stroke-width='0.5' rx='0.5'
                          stroke-dasharray='2,1.5'/>
                    <rect x='12' y='4' width='8' height='4' fill='#ddd' stroke='#444' stroke-width='0.6'/>
                    <rect x='80' y='4' width='8' height='4' fill='#ddd' stroke='#444' stroke-width='0.6'/>
                    <rect x='12' y='40' width='8' height='4' fill='#ddd' stroke='#444' stroke-width='0.6'/>
                    <rect x='80' y='40' width='8' height='4' fill='#ddd' stroke='#444' stroke-width='0.6'/>
                    <line x1='12' y1='49' x2='88' y2='49' stroke='#555' stroke-width='0.5'/>
                    <line x1='12' y1='46' x2='12' y2='51' stroke='#555' stroke-width='0.5'/>
                    <line x1='88' y1='46' x2='88' y2='51' stroke='#555' stroke-width='0.5'/>
                    <text x='50' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{width:F0}</text>
                    <line x1='7' y1='4' x2='7' y2='44' stroke='#555' stroke-width='0.5'/>
                    <line x1='4' y1='4' x2='9' y2='4' stroke='#555' stroke-width='0.5'/>
                    <line x1='4' y1='44' x2='9' y2='44' stroke='#555' stroke-width='0.5'/>
                    <text x='6' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial'
                          transform='rotate(-90,6,26)'>{height:F0}</text>
                    <text x='50' y='26' font-size='6' text-anchor='middle' fill='#555' font-family='Arial'>Anwis</text>
                </svg>";
            }
            else if (name == "На навесах")
            {
                // Hinged/swing-out net — rectangle with hinge pins on one side
                return $@"<svg width='100' height='54' viewBox='0 0 100 54'>
                    <rect x='16' y='4' width='72' height='40' fill='#f5f5f5' stroke='#444' stroke-width='1' rx='1.5'/>
                    <rect x='20' y='8' width='64' height='32' fill='none' stroke='#aaa' stroke-width='0.5' rx='0.5'
                          stroke-dasharray='2,1.5'/>
                    <circle cx='14' cy='10' r='2.5' fill='#ccc' stroke='#444' stroke-width='0.7'/>
                    <circle cx='14' cy='38' r='2.5' fill='#ccc' stroke='#444' stroke-width='0.7'/>
                    <line x1='14' y1='12' x2='14' y2='36' stroke='#666' stroke-width='0.6' stroke-dasharray='3,2'/>
                    <line x1='16' y1='49' x2='88' y2='49' stroke='#555' stroke-width='0.5'/>
                    <line x1='16' y1='46' x2='16' y2='51' stroke='#555' stroke-width='0.5'/>
                    <line x1='88' y1='46' x2='88' y2='51' stroke='#555' stroke-width='0.5'/>
                    <text x='52' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{width:F0}</text>
                    <line x1='7' y1='4' x2='7' y2='44' stroke='#555' stroke-width='0.5'/>
                    <line x1='4' y1='4' x2='9' y2='4' stroke='#555' stroke-width='0.5'/>
                    <line x1='4' y1='44' x2='9' y2='44' stroke='#555' stroke-width='0.5'/>
                    <text x='6' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial'
                          transform='rotate(-90,6,26)'>{height:F0}</text>
                    <text x='52' y='26' font-size='5.5' text-anchor='middle' fill='#555' font-family='Arial'>навесы</text>
                </svg>";
            }
            else if (name == "Козырёк")
            {
                // Awning/canopy — net rectangle with angled top
                return $@"<svg width='100' height='54' viewBox='0 0 100 54'>
                    <rect x='12' y='4' width='76' height='40' fill='#f5f5f5' stroke='#444' stroke-width='1' rx='1.5'/>
                    <line x1='12' y1='10' x2='88' y2='10' stroke='#999' stroke-width='0.5' stroke-dasharray='3,2'/>
                    <line x1='12' y1='49' x2='88' y2='49' stroke='#555' stroke-width='0.5'/>
                    <line x1='12' y1='46' x2='12' y2='51' stroke='#555' stroke-width='0.5'/>
                    <line x1='88' y1='46' x2='88' y2='51' stroke='#555' stroke-width='0.5'/>
                    <text x='50' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{width:F0}</text>
                    <line x1='7' y1='4' x2='7' y2='44' stroke='#555' stroke-width='0.5'/>
                    <line x1='4' y1='4' x2='9' y2='4' stroke='#555' stroke-width='0.5'/>
                    <line x1='4' y1='44' x2='9' y2='44' stroke='#555' stroke-width='0.5'/>
                    <text x='6' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial'
                          transform='rotate(-90,6,26)'>{height:F0}</text>
                    <text x='50' y='28' font-size='6.5' text-anchor='middle' fill='#555' font-family='Arial'>козырёк</text>
                </svg>";
            }
            else if (name == "Короб")
            {
                // Box frame — net rectangle with thick frame
                return $@"<svg width='100' height='54' viewBox='0 0 100 54'>
                    <rect x='12' y='4' width='76' height='40' fill='#f5f5f5' stroke='#444' stroke-width='1.5' rx='1'/>
                    <rect x='16' y='8' width='68' height='32' fill='none' stroke='#888' stroke-width='1.2' rx='0.5'/>
                    <line x1='12' y1='49' x2='88' y2='49' stroke='#555' stroke-width='0.5'/>
                    <line x1='12' y1='46' x2='12' y2='51' stroke='#555' stroke-width='0.5'/>
                    <line x1='88' y1='46' x2='88' y2='51' stroke='#555' stroke-width='0.5'/>
                    <text x='50' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{width:F0}</text>
                    <line x1='7' y1='4' x2='7' y2='44' stroke='#555' stroke-width='0.5'/>
                    <line x1='4' y1='4' x2='9' y2='4' stroke='#555' stroke-width='0.5'/>
                    <line x1='4' y1='44' x2='9' y2='44' stroke='#555' stroke-width='0.5'/>
                    <text x='6' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial'
                          transform='rotate(-90,6,26)'>{height:F0}</text>
                    <text x='50' y='26' font-size='7' text-anchor='middle' fill='#444' font-family='Arial'
                          font-weight='500'>короб</text>
                </svg>";
            }
            else if (name == "ПСУЛ")
            {
                // ПСУЛ — sealing tape around perimeter: rectangle with thick border
                return $@"<svg width='100' height='54' viewBox='0 0 100 54'>
                    <rect x='10' y='2' width='80' height='44' fill='none' stroke='#444' stroke-width='3' rx='1'/>
                    <rect x='16' y='8' width='68' height='32' fill='#f5f5f5' stroke='#999' stroke-width='0.5' rx='0.5'
                          stroke-dasharray='2,1.5'/>
                    <line x1='10' y1='51' x2='90' y2='51' stroke='#555' stroke-width='0.5'/>
                    <line x1='10' y1='48' x2='10' y2='53' stroke='#555' stroke-width='0.5'/>
                    <line x1='90' y1='48' x2='90' y2='53' stroke='#555' stroke-width='0.5'/>
                    <text x='50' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{width:F0}</text>
                    <line x1='5' y1='2' x2='5' y2='46' stroke='#555' stroke-width='0.5'/>
                    <line x1='3' y1='2' x2='7' y2='2' stroke='#555' stroke-width='0.5'/>
                    <line x1='3' y1='46' x2='7' y2='46' stroke='#555' stroke-width='0.5'/>
                    <text x='4' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial'
                          transform='rotate(-90,4,26)'>{height:F0}</text>
                    <text x='50' y='26' font-size='5.5' text-anchor='middle' fill='#555' font-family='Arial'>ПСУЛ</text>
                </svg>";
            }
            else if (name == "Откос материал")
            {
                // Откос — window reveal/slope cross-section
                return $@"<svg width='100' height='36' viewBox='0 0 100 36'>
                    <rect x='10' y='4' width='80' height='28' fill='#f5f5f5' stroke='#444' stroke-width='1' rx='1'/>
                    <line x1='10' y1='4' x2='20' y2='12' stroke='#666' stroke-width='0.8'/>
                    <line x1='90' y1='4' x2='80' y2='12' stroke='#666' stroke-width='0.8'/>
                    <line x1='10' y1='32' x2='20' y2='24' stroke='#666' stroke-width='0.8'/>
                    <line x1='90' y1='32' x2='80' y2='24' stroke='#666' stroke-width='0.8'/>
                    <line x1='10' y1='33' x2='90' y2='33' stroke='#555' stroke-width='0.5'/>
                    <line x1='10' y1='30' x2='10' y2='34' stroke='#555' stroke-width='0.5'/>
                    <line x1='90' y1='30' x2='90' y2='34' stroke='#555' stroke-width='0.5'/>
                    <text x='50' y='35' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{width:F0}</text>
                    <line x1='5' y1='4' x2='5' y2='32' stroke='#555' stroke-width='0.5'/>
                    <line x1='3' y1='4' x2='7' y2='4' stroke='#555' stroke-width='0.5'/>
                    <line x1='3' y1='32' x2='7' y2='32' stroke='#555' stroke-width='0.5'/>
                    <text x='4' y='20' font-size='6' text-anchor='middle' fill='#333' font-family='Arial'
                          transform='rotate(-90,4,20)'>{height:F0}</text>
                    <text x='50' y='20' font-size='5.5' text-anchor='middle' fill='#555' font-family='Arial'>откос</text>
                </svg>";
            }
            else if (name == "Работа")
            {
                // Работа — labor/work, no dimensional drawing
                return $@"<svg width='40' height='20' viewBox='0 0 40 20'>
                    <text x='20' y='14' font-size='8' text-anchor='middle' fill='#666' font-family='Arial'>раб.</text>
                </svg>";
            }
            else if (name == "Брус")
            {
                // Брус — wooden beam profile
                return $@"<svg width='40' height='20' viewBox='0 0 40 20'>
                    <text x='20' y='14' font-size='8' text-anchor='middle' fill='#666' font-family='Arial'>брус</text>
                </svg>";
            }
            else if (name == "Пояс")
            {
                // Пояс — belt/band element
                return $@"<svg width='40' height='20' viewBox='0 0 40 20'>
                    <text x='20' y='14' font-size='8' text-anchor='middle' fill='#666' font-family='Arial'>пояс</text>
                </svg>";
            }
            else if (name == "Доставка")
            {
                // Доставка — delivery service
                return $@"<svg width='40' height='20' viewBox='0 0 40 20'>
                    <text x='20' y='14' font-size='8' text-anchor='middle' fill='#666' font-family='Arial'>дост.</text>
                </svg>";
            }
            else if (name == "Уплотнение")
            {
                return $@"<svg width='40' height='20' viewBox='0 0 40 20'>
                    <text x='20' y='14' font-size='8' text-anchor='middle' fill='#666' font-family='Arial'>уплотн.</text>
                </svg>";
            }
            else
            {
                // Fallback generic rectangle (for any future products)
                return $@"<svg width='100' height='54' viewBox='0 0 100 54'>
                    <rect x='12' y='4' width='76' height='40' fill='#f5f5f5' stroke='#444' stroke-width='1' rx='1.5'/>
                    <rect x='16' y='8' width='68' height='32' fill='none' stroke='#aaa' stroke-width='0.4' rx='0.5'
                          stroke-dasharray='3,2'/>
                    <line x1='12' y1='49' x2='88' y2='49' stroke='#555' stroke-width='0.5'/>
                    <line x1='12' y1='46' x2='12' y2='51' stroke='#555' stroke-width='0.5'/>
                    <line x1='88' y1='46' x2='88' y2='51' stroke='#555' stroke-width='0.5'/>
                    <text x='50' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{width:F0}</text>
                    <line x1='7' y1='4' x2='7' y2='44' stroke='#555' stroke-width='0.5'/>
                    <line x1='4' y1='4' x2='9' y2='4' stroke='#555' stroke-width='0.5'/>
                    <line x1='4' y1='44' x2='9' y2='44' stroke='#555' stroke-width='0.5'/>
                    <text x='6' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial'
                          transform='rotate(-90,6,26)'>{height:F0}</text>
                </svg>";
            }
        }

        // `internal` (not `private`) so the regression test in
        // PrintServiceTests can call it directly. LoadTemplate() prefers
        // the embedded resource, so testing through GenerateKpHtml would
        // silently exercise the file-based template and never catch
        // duplication in this inline-fallback string.
        internal string GetInlineTemplate()
        {
            return @"<!DOCTYPE html>
<html lang='ru'>
<head>
<meta charset='UTF-8'>
<title>Коммерческое предложение</title>
<style>
  @page { size: A4; margin: 0; }
  @media print { .no-print { display: none !important; } body { margin: 0; } }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 9pt; color: #111; line-height: 1.45; padding: 30mm 16mm 14mm 16mm; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
  .doc-header { text-align: center; padding-bottom: 10px; margin-bottom: 16px; border-bottom: 2.5px solid #2a2a2a; }
  .doc-header h1 { font-size: 13.5pt; font-weight: 700; letter-spacing: 2px; text-transform: uppercase; color: #111; margin-bottom: 5px; }
  .doc-header .contract-line { font-size: 9pt; color: #444; }
  .client-block { margin-bottom: 16px; }
  .client-row { display: flex; align-items: baseline; margin-bottom: 4px; font-size: 9pt; }
  .client-label { width: 82px; font-weight: 600; color: #333; flex-shrink: 0; font-size: 8.5pt; }
  .client-value { color: #111; flex: 1; border-bottom: 1px solid #999; padding-bottom: 1px; min-height: 14px; word-wrap: break-word; overflow-wrap: break-word; }
  table { width: 100%; border-collapse: collapse; font-size: 8.5pt; table-layout: fixed; }
  th, td { border: 1px solid #999; padding: 4px 5px; vertical-align: middle; overflow: hidden; }
  td { white-space: nowrap; }
  th { background: #d8d8d8; font-weight: 600; font-size: 7.5pt; text-align: center; color: #1a1a1a; padding: 5px 4px; border-bottom: 2px solid #666; }
  .center { text-align: center; }
  .right { text-align: right; }
  /* Installation marks in the КП table: explicit pure-black colour so the
     checkmark/cross never drifts to a non-black tone on a dark/grayscale
     print or a different page background. 1.15em keeps the icon readable
     while staying in rhythm with the 8.5pt cell text. */
  .install-mark { color: #000000; font-weight: 600; font-size: 1.15em; }
  .name-cell { text-align: left; padding-left: 6px !important; font-weight: 500; white-space: normal; word-wrap: break-word; overflow-wrap: break-word; }
  tbody tr:nth-child(even) td { background: #f0f0f0; }
  .total-row td { font-weight: 700; background: #d5d5d5 !important; border-top: 2px solid #444; font-size: 9pt; }
  .drawing { text-align: center; padding: 2px !important; }
  .drawing svg { display: block; margin: auto; max-width: 100%; }
  .total-section { margin-top: 14px; padding-top: 10px; border-top: 2px solid #1a1a1a; }
  .total-main { display: flex; justify-content: flex-end; align-items: baseline; gap: 6px; }
  .total-main .label { font-size: 10pt; font-weight: 700; color: #111; letter-spacing: 0.5px; }
  .total-main .amount { font-size: 13pt; font-weight: 700; color: #111; }
  .total-main .currency { font-size: 9.5pt; color: #4a4a4a; font-weight: 500; }
  .amount-words { text-align: right; font-size: 8.5pt; color: #444; margin-top: 4px; font-style: italic; }
  .additional-kp { margin-top: 10px; font-size: 9pt; color: #333; font-weight: 500; }
  .additional-kp .additional-kp-title { font-weight: 700; font-size: 9pt; color: #1a1a1a; margin-bottom: 4px; }
  .additional-kp .additional-kp-row { display: flex; justify-content: space-between; align-items: baseline; padding: 3px 0; }
  .additional-kp .additional-kp-sum { font-variant-numeric: tabular-nums; font-weight: 600; color: #1a1a1a; }
  .additional-total { background: #f5f5f5; border: 1px solid #ccc; border-radius: 4px; padding: 10px 14px; }
  .total-row-item { display: flex; justify-content: space-between; align-items: baseline; padding: 3px 0; font-size: 9pt; color: #333; }
  .total-row-item .total-row-label { font-weight: 500; }
  .total-row-item .total-row-amount { font-weight: 600; font-variant-numeric: tabular-nums; color: #1a1a1a; }
  .total-row-item.total-row-grand { border-top: 1.5px solid #2a2a2a; margin-top: 5px; padding-top: 6px; font-size: 10.5pt; }
  .total-row-item.total-row-grand .total-row-label { font-weight: 700; color: #1a1a1a; letter-spacing: 0.5px; }
  .total-row-item.total-row-grand .total-row-amount { font-weight: 700; font-size: 12pt; color: #1a1a1a; }
  .notes-block { margin-top: 12px; padding: 8px 12px; background: #f9f9f9; border-left: 3px solid #888; font-size: 8.5pt; color: #333; line-height: 1.5; white-space: normal; word-wrap: break-word; overflow-wrap: break-word; -webkit-text-size-adjust: 100%; text-size-adjust: 100%; }
  .notes-block .notes-title { font-weight: 700; font-size: 8.5pt; color: #2a2a2a; margin-bottom: 3px; text-transform: uppercase; letter-spacing: 0.3px; }
  .terms-block { margin-top: 20px; padding-top: 10px; border-top: 1px solid #aaa; font-size: 7.5pt; color: #444; line-height: 1.65; page-break-inside: avoid; }
  .terms-block .terms-title { font-weight: 700; color: #222; margin-bottom: 5px; font-size: 8pt; text-transform: uppercase; letter-spacing: 0.5px; }
  .terms-block ul { list-style: none; padding: 0; }
  .terms-block li { padding-left: 14px; position: relative; margin-bottom: 2px; }
  .terms-block li::before { content: '\2013'; position: absolute; left: 2px; color: #999; }
  .signature-block { margin-top: 32px; display: flex; justify-content: space-between; page-break-inside: avoid; }
  .sig-side { width: 44%; }
  .sig-role { font-size: 9pt; font-weight: 600; color: #1a1a1a; margin-bottom: 2px; }
  .sig-line { border-bottom: 1px solid #1a1a1a; margin-top: 44px; margin-bottom: 3px; width: 85%; }
  .sig-stamp { font-size: 7.5pt; color: #888; text-align: right; width: 85%; margin-left: auto; }
  .bottom-group { page-break-inside: avoid; }
  .no-print { margin-top: 20px; text-align: center; }
  .no-print button { padding: 10px 32px; font-size: 11pt; background: #111; color: white; border: none; border-radius: 4px; cursor: pointer; }
  .no-print button:hover { background: #444; }
</style>
</head>
<body>
<div class='doc-header'>
  <h1>Коммерческое предложение</h1>
  <div class='contract-line'>Договор &#8470; <b>{{CONTRACT_NUMBER}}</b> &nbsp; от <b>{{CONTRACT_DATE}}</b></div>
</div>
<div class='client-block'>
  <div class='client-row'><span class='client-label'>Заказчик:</span><span class='client-value'>{{CLIENT_NAME}}</span></div>
  <div class='client-row'><span class='client-label'>Телефон:</span><span class='client-value'>{{CLIENT_PHONE}}</span></div>
  <div class='client-row'><span class='client-label'>Адрес:</span><span class='client-value'>{{CLIENT_ADDRESS}}</span></div>
</div>
<table>
  <colgroup>
    <col style='width:3.5%'><col style='width:15%'><col style='width:7.5%'><col style='width:6.5%'>
    <col style='width:6.5%'><col style='width:7%'><col style='width:7.5%'><col style='width:9%'>
    <col style='width:4%'><col style='width:9.5%'><col style='width:11.5%'><col style='width:14%'>
  </colgroup>
  <thead><tr><th>&#8470;</th><th>Наименование</th><th>Цвет</th><th>Ш, мм</th><th>В, мм</th><th>Кол-во</th><th>Монтаж</th><th>Площ./Дл.</th><th>Ед.</th><th>Цена</th><th>Сумма</th><th>Чертёж</th></tr></thead>
  <tbody>
    {{ROWS}}
    <tr class='total-row'><td colspan='10' class='right' style='padding-right:8px;'>ИТОГО:</td><td class='right' style='padding-right:6px;'>{{TOTAL_AMOUNT}}</td><td></td></tr>
  </tbody>
</table>
<div class='total-section'>
  <div class='total-main'><span class='label'>ИТОГО:</span><span class='amount'>{{TOTAL_AMOUNT}}</span><span class='currency'>руб.</span></div>
  <div class='amount-words'>{{AMOUNT_IN_WORDS}}</div>
</div>
{{ADDITIONAL_KP_BLOCK}}
{{NOTES_BLOCK}}
<div class='bottom-group'>
<div class='terms-block'>
  <div class='terms-title'>Условия</div>
  <ul>
    <li>Срок действия коммерческого предложения &mdash; 5 рабочих дней.</li>
    <li>Оплата производится на основании счёта.</li>
    <li>Цены указаны с учётом стоимости материалов.</li>
  </ul>
</div>
<div class='signature-block'>
  <div class='sig-side'><div class='sig-role'>Исполнитель</div><div class='sig-line'></div></div>
  <div class='sig-side'><div class='sig-role'>Заказчик</div><div class='sig-line'></div></div>
</div>
</div>

</body>
</html>";
        }
    }
}
