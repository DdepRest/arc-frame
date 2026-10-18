using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
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
            string amountInWords,
            PrintSettings? attemptSettings = null)
            => _pdfExportService.Export(filePath, items, clientInfo, totalAmount, amountInWords, attemptSettings);

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

        /// <summary>Returns the actual installed Windows queues for direct selection.</summary>
        public static List<PrintQueue> GetInstalledPrintQueues()
            => PrintQueueManager.GetInstalledPrintQueues();

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

        /// <summary>
        /// Выполняет прямую печать заказа без открытия оверлея предпросмотра.
        /// Разрешает очередь печати (по имени или по умолчанию), строит документы
        /// и отправляет задание в спулер Windows.
        /// </summary>
        public static PrintResult PrintDirect(
            List<OrderItem> items,
            ClientInfo clientInfo,
            PrintSettings settings,
            out string resolvedPrinterName)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (clientInfo == null) throw new ArgumentNullException(nameof(clientInfo));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var queue = string.IsNullOrWhiteSpace(settings.PrinterName)
                ? ResolvePrintQueue(null)
                : ResolvePrintQueue(settings.PrinterName);
            if (queue == null)
            {
                resolvedPrinterName = string.Empty;
                return new PrintResult
                {
                    Type = PrintResultType.PrinterError,
                    UserMessage = "Не удалось определить принтер. Подключите принтер или откройте предпросмотр.",
                    IsRetryable = false
                };
            }

            resolvedPrinterName = queue.FullName;

            var activeItems = items.Where(i => !string.IsNullOrEmpty(i.Name) && i.IsActive && i.Total > 0).ToList();
            if (activeItems.Count == 0)
            {
                return new PrintResult
                {
                    Type = PrintResultType.Unknown,
                    UserMessage = "Нет позиций для печати.",
                    IsRetryable = false
                };
            }

            double total = activeItems.Sum(i => i.TotalWithDeduction);
            string amountInWords = AmountInWordsService.Convert(total);
            string contractNumber = clientInfo.ContractNumber ?? "";

            var flowDoc = new FlowDocumentBuilder().Build(activeItems, clientInfo, total, amountInWords);
            if (flowDoc == null)
            {
                return new PrintResult
                {
                    Type = PrintResultType.Unknown,
                    UserMessage = "Не удалось сформировать печатный документ.",
                    IsRetryable = false
                };
            }

            PrintTicket ticket;
            try
            {
                ticket = queue.DefaultPrintTicket.Clone();
                ticket.OutputColor = settings.Color ? OutputColor.Color : OutputColor.Monochrome;
                ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
                ticket.PageOrder = PageOrder.Standard;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PrintService] PrintTicket config failed: {ex.Message}");
                ticket = queue.DefaultPrintTicket;
            }

            FixedDocument fixedDoc;
            try
            {
                fixedDoc = BuildFixedDocument(flowDoc, settings, contractNumber, DateTime.Now);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PrintService] BuildFixedDocument failed: {ex}");
                return new PrintResult
                {
                    Type = PrintResultType.Unknown,
                    UserMessage = $"Ошибка подготовки документа к печати: {ex.Message}",
                    DebugMessage = ex.ToString(),
                    IsRetryable = false
                };
            }

            string jobName = string.IsNullOrWhiteSpace(contractNumber) ? "КП" : $"КП №{contractNumber.Trim()}";
            return SendToQueue(queue, jobName, fixedDoc.DocumentPaginator, ticket);
        }

        /// <summary>
        /// Выполняет прямую печать заказа асинхронно в выделенном STA-потоке,
        /// полностью исключая зависание и блокировку основного UI-потока.
        /// </summary>
        public static Task<(PrintResult Result, string PrinterName)> PrintDirectAsync(
            List<OrderItem> items,
            ClientInfo clientInfo,
            PrintSettings settings)
        {
            var tcs = new TaskCompletionSource<(PrintResult, string)>();
            var thread = new Thread(() =>
            {
                try
                {
                    var result = PrintDirect(items, clientInfo, settings, out string resolvedName);
                    tcs.SetResult((result, resolvedName));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PrintService] PrintDirectAsync error: {ex}");
                    tcs.SetResult((new PrintResult
                    {
                        Type = PrintResultType.Unknown,
                        UserMessage = $"Ошибка печати: {ex.Message}",
                        DebugMessage = ex.ToString(),
                        IsRetryable = false
                    }, string.Empty));
                }
            })
            {
                IsBackground = true,
                Name = "DirectPrintWorkerThread"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        // Drawing helpers (preserved for backward compatibility).

        /// <summary>Wraps a UI element for centering in a table cell.</summary>
        public static UIElement WrapForCentering(UIElement content, double minHeight = 0)
            => DrawingService.WrapForCentering(content, minHeight);

        /// <summary>Builds a WPF Image for a drawing cell.</summary>
        public static Image CreateDrawingImageElement(string name, double width, double height, double displayWidth = 70)
            => DrawingService.CreateDrawingImageElement(name, width, height, displayWidth);
    }
}
