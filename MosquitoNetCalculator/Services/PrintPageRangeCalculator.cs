using System;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Pure helpers for print-preview page selection.
    /// Page numbers are one-based, matching WPF's FlowDocumentPageViewer.
    /// </summary>
    internal static class PrintPageRangeCalculator
    {
        public static int Count(
            PageMode mode,
            int totalPages,
            int pageFrom,
            int pageTo,
            int singlePage)
        {
            totalPages = Math.Max(0, totalPages);
            if (totalPages == 0)
                return 0;

            if (mode == PageMode.All)
                return totalPages;

            if (mode == PageMode.Single)
                return singlePage >= 1 && singlePage <= totalPages ? 1 : 0;

            int first = Math.Max(1, Math.Min(pageFrom, pageTo));
            int last = Math.Min(totalPages, Math.Max(pageFrom, pageTo));
            return Math.Max(0, last - first + 1);
        }

        public static bool Contains(
            PageMode mode,
            int currentPage,
            int totalPages,
            int pageFrom,
            int pageTo,
            int singlePage)
        {
            if (currentPage < 1 || currentPage > totalPages)
                return false;

            return mode switch
            {
                PageMode.All => true,
                PageMode.Single => currentPage == singlePage,
                _ => currentPage >= Math.Min(pageFrom, pageTo)
                     && currentPage <= Math.Max(pageFrom, pageTo)
            };
        }
    }
}
