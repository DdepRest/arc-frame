using System;
using System.Collections.Generic;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Единая логика выбора исходных страниц по <see cref="PrintSettings.Pages"/>
    /// (All / Single / Range) — общая для физической печати (<see cref="FixedDocumentBuilder"/>)
    /// и PDF-экспорта (<see cref="PdfExportService"/>). Клэмпит границы к допустимому
    /// диапазону, поэтому некорректные значения UI не ломают вывод.
    /// </summary>
    internal static class PageSelection
    {
        /// <summary>
        /// Возвращает список 0-based индексов выбранных страниц исходного документа
        /// (1..sourceCount → 0..sourceCount-1) в порядке вывода.
        /// </summary>
        public static List<int> GetSelectedSourcePages(PrintSettings settings, int sourceCount)
        {
            if (sourceCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceCount), "Source document has no pages.");

            switch (settings.Pages)
            {
                case PageMode.Single:
                {
                    int p = Math.Clamp(settings.SinglePage - 1, 0, sourceCount - 1);
                    return new List<int> { p };
                }
                case PageMode.Range:
                {
                    int from = Math.Clamp(settings.PageFrom - 1, 0, sourceCount - 1);
                    int to = Math.Clamp(settings.PageTo - 1, 0, sourceCount - 1);
                    if (from > to) (from, to) = (to, from);
                    var list = new List<int>(to - from + 1);
                    for (int i = from; i <= to; i++) list.Add(i);
                    return list;
                }
                case PageMode.All:
                default:
                {
                    var list = new List<int>(sourceCount);
                    for (int i = 0; i < sourceCount; i++) list.Add(i);
                    return list;
                }
            }
        }
    }
}
