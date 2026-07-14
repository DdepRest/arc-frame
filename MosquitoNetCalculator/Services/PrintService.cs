using System;
using System.Collections.Generic;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Facade for КП printing. Delegates to specialized components:
    ///  - FlowDocumentBuilder  → BuildFlowDocument
    ///  - PdfExportService     → ExportPdf
    ///  - FixedDocumentBuilder → BuildFixedDocument
    ///  - PrintQueueManager    → SendToQueue, printer discovery
    ///  - DrawingService       → product drawings
    /// </summary>
    public class PrintService
    {
        private readonly FlowDocumentBuilder _flowDocumentBuilder = new();
        private readonly PdfExportService _pdfExportService = new();

        /// <summary>Builds an A4 FlowDocument for in-app preview and physical printing.</summary>
        public FlowDocument? BuildFlowDocument(
            List<OrderItem> items,
            ClientInfo clientInfo,
            double totalAmount,
            string amountInWords)
            => _flowDocumentBuilder.Build(items, clientInfo, totalAmount, amountInWords);

        /// <summary>Exports a КП as a PDF file via QuestPDF.</summary>
        public void ExportPdf(
            string filePath,
            List<OrderItem> items,
            ClientInfo clientInfo,
            double totalAmount,
            string amountInWords)
            => _pdfExportService.Export(filePath, items, clientInfo, totalAmount, amountInWords);

        // Static proxies to specialized components (preserved for backward compatibility).

        /// <summary>Sends a print job directly to a print queue.</summary>
        public static PrintResult SendToQueue(
            PrintQueue queue,
            string jobName,
            DocumentPaginator paginator,
            PrintTicket ticket)
            => PrintQueueManager.SendToQueue(queue, jobName, paginator, ticket);

        /// <summary>Returns installed printer names.</summary>
        public static List<string> GetInstalledPrinterNames()
            => PrintQueueManager.GetInstalledPrinterNames();

        /// <summary>Returns the default printer name.</summary>
        public static string? GetDefaultPrinterName()
            => PrintQueueManager.GetDefaultPrinterName();

        /// <summary>Resolves a PrintQueue by name (or default).</summary>
        public static PrintQueue? ResolvePrintQueue(string? printerName)
            => PrintQueueManager.ResolvePrintQueue(printerName);

        /// <summary>Builds a FixedDocument from a FlowDocument with settings.</summary>
        public static FixedDocument BuildFixedDocument(
            FlowDocument sourceDoc,
            PrintSettings settings,
            string contractNumber,
            DateTime contractDate)
            => FixedDocumentBuilder.Build(sourceDoc, settings, contractNumber, contractDate);

        // Drawing helpers (preserved for backward compatibility).

        /// <summary>Wraps a UI element for centering in a table cell.</summary>
        public static UIElement WrapForCentering(UIElement content, double minHeight = 0)
            => DrawingService.WrapForCentering(content, minHeight);

        /// <summary>Builds a WPF Image for a drawing cell.</summary>
        public static Image CreateDrawingImageElement(string name, double width, double height, double displayWidth = 70)
            => DrawingService.CreateDrawingImageElement(name, width, height, displayWidth);
    }
}
