using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Printing;
using System.Windows.Documents;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Manages Windows print queues: discovery, default printer resolution,
    /// and sending a DocumentPaginator to a PrintQueue.
    /// </summary>
    public static class PrintQueueManager
    {
        /// <summary>
        /// Отправляет задание печати напрямую в <paramref name="queue"/>,
        /// классифицирует исключения и возвращает <see cref="PrintResult"/>.
        /// </summary>
        public static PrintResult SendToQueue(
            PrintQueue queue,
            string jobName,
            DocumentPaginator paginator,
            PrintTicket ticket)
        {
            if (queue == null) throw new ArgumentNullException(nameof(queue));
            if (paginator == null) throw new ArgumentNullException(nameof(paginator));
            if (ticket == null) throw new ArgumentNullException(nameof(ticket));

            try
            {
                var writer = PrintQueue.CreateXpsDocumentWriter(queue);
                writer.Write(paginator, ticket);
                return PrintResult.Ok();
            }
            catch (PrintQueueException pqEx)
            {
                var msg = pqEx.Message.ToLowerInvariant();
                var (type, userMsg) = msg switch
                {
                    _ when msg.Contains("offline") || msg.Contains("отключ") =>
                        (PrintResultType.PrinterOffline,
                         $"Принтер «{queue.Name}» не подключён или выключен."),
                    _ when msg.Contains("paper") || msg.Contains("бумаг") =>
                        (PrintResultType.PrinterOutOfPaper,
                         $"В принтере «{queue.Name}» закончилась бумага."),
                    _ when msg.Contains("toner") || msg.Contains("тонер") || msg.Contains("чернил") =>
                        (PrintResultType.PrinterTonerLow,
                         $"В принтере «{queue.Name}» низкий уровень тонера/чернил."),
                    _ =>
                        (PrintResultType.PrinterError,
                         $"Ошибка принтера «{queue.Name}»: {pqEx.Message}")
                };
                Debug.WriteLine($"[PrintQueueManager] PrintQueueException ({type}): {pqEx.Message}");
                return new PrintResult
                {
                    Type = type,
                    UserMessage = userMsg,
                    DebugMessage = pqEx.ToString(),
                    IsRetryable = true
                };
            }
            catch (PrintSystemException psEx)
            {
                Debug.WriteLine($"[PrintQueueManager] PrintSystemException: {psEx.Message}");
                return new PrintResult
                {
                    Type = PrintResultType.SpoolerStopped,
                    UserMessage = "Служба печати Windows остановлена или недоступна. " +
                                  "Проверьте, запущен ли «Диспетчер очереди печати» (services.msc).",
                    DebugMessage = psEx.ToString(),
                    IsRetryable = false
                };
            }
            catch (UnauthorizedAccessException uaEx)
            {
                Debug.WriteLine($"[PrintQueueManager] UnauthorizedAccess: {uaEx.Message}");
                return new PrintResult
                {
                    Type = PrintResultType.AccessDenied,
                    UserMessage = $"Нет доступа к принтеру «{queue.Name}». " +
                                  "Обратитесь к системному администратору.",
                    DebugMessage = uaEx.ToString(),
                    IsRetryable = false
                };
            }
            catch (InvalidOperationException ioEx)
            {
                Debug.WriteLine($"[PrintQueueManager] InvalidOperation: {ioEx.Message}");
                return new PrintResult
                {
                    Type = PrintResultType.QueueError,
                    UserMessage = $"Очередь печати «{queue.Name}» в недопустимом состоянии. " +
                                  "Попробуйте перезапустить очередь печати.",
                    DebugMessage = ioEx.ToString(),
                    IsRetryable = false
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintQueueManager] Unexpected: {ex}");
                return new PrintResult
                {
                    Type = PrintResultType.Unknown,
                    UserMessage = $"Неожиданная ошибка печати: {ex.Message}",
                    DebugMessage = ex.ToString(),
                    IsRetryable = false
                };
            }
        }

        /// <summary>
        /// Возвращает список установленных принтеров (имена), отсортированный по алфавиту.
        /// Включает локальные и сетевые подключения.
        /// </summary>
        public static List<string> GetInstalledPrinterNames()
        {
            var result = new List<string>();
            try
            {
                var server = new LocalPrintServer();
                var queues = server.GetPrintQueues(new[]
                {
                    EnumeratedPrintQueueTypes.Local,
                    EnumeratedPrintQueueTypes.Connections
                });
                result.AddRange(queues.Select(q => q.FullName));
                result.Sort(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintQueueManager] GetInstalledPrinterNames failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Возвращает имя принтера по умолчанию или null, если не найден.
        /// </summary>
        public static string? GetDefaultPrinterName()
        {
            try
            {
                var server = new LocalPrintServer();
                return server.DefaultPrintQueue?.FullName;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintQueueManager] GetDefaultPrinterName failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Получает PrintQueue по имени. Если не найден — возвращает DefaultPrintQueue.
        /// Не диспозит LocalPrintServer — PrintQueue удерживает ссылку на спулер.
        /// </summary>
        public static PrintQueue? ResolvePrintQueue(string? printerName)
        {
            try
            {
                var server = new LocalPrintServer();
                if (!string.IsNullOrEmpty(printerName))
                {
                    var q = server.GetPrintQueue(printerName);
                    if (q != null) return q;
                }
                return server.DefaultPrintQueue;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintQueueManager] ResolvePrintQueue failed: {ex.Message}");
                try
                {
                    var server = new LocalPrintServer();
                    return server.DefaultPrintQueue;
                }
                catch { return null; }
            }
        }
    }
}
