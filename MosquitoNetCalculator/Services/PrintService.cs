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
    public partial class PrintService
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
        }    }
}
