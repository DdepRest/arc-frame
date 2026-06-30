using System;

namespace MosquitoNetCalculator.Services
{
    public partial class PrintService
    {
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
    }
}
