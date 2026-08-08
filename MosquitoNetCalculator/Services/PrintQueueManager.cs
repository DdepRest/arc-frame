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
            var queues = GetInstalledPrintQueues();
            try
            {
                return queues
                    .Select(ReadQueueFullName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            }
            finally
            {
                DisposeQueues(queues);
            }
        }

        /// <summary>
        /// Returns the actual Windows queue objects used to populate the printer
        /// picker. Keeping these instances avoids losing a network/local-port
        /// binding by resolving the selected display name a second time.
        /// </summary>
        public static List<PrintQueue> GetInstalledPrintQueues()
        {
            var result = new List<PrintQueue>();
            try
            {
                var server = new LocalPrintServer();
                var queues = server.GetPrintQueues(new[]
                {
                    EnumeratedPrintQueueTypes.Local,
                    EnumeratedPrintQueueTypes.Connections
                });
                result.AddRange(queues);
                result.Sort((left, right) =>
                    string.Compare(ReadQueueFullName(left), ReadQueueFullName(right), StringComparison.Ordinal));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintQueueManager] GetInstalledPrintQueues failed: {ex.Message}");
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
        /// Получает PrintQueue по имени.
        /// Для пустого имени возвращает DefaultPrintQueue; если явно указанная
        /// очередь не найдена, возвращает null, чтобы не отправить документ
        /// молча на другой принтер.
        /// Не диспозит LocalPrintServer — PrintQueue удерживает ссылку на спулер.
        ///
        /// Для сетевых подключений очередь обязательно разрешается через
        /// перечисление Local + Connections. Прямой GetPrintQueue(name) может
        /// вернуть объект без корректного контекста подключённой сетевой очереди;
        /// XpsDocumentWriter тогда отправляет задание в Windows со статусом
        /// «Принтер не в сети», хотя тот же принтер работает в других приложениях.
        /// </summary>
        public static PrintQueue? ResolvePrintQueue(string? printerName)
        {
            try
            {
                var server = new LocalPrintServer();
                if (!string.IsNullOrWhiteSpace(printerName))
                {
                    var queues = server.GetPrintQueues(new[]
                    {
                        EnumeratedPrintQueueTypes.Local,
                        EnumeratedPrintQueueTypes.Connections
                    });

                    // FullName preserves the server/connection context. Resolve
                    // the exact selected identity first; do not substitute the
                    // default queue when an explicit target cannot be resolved.
                    var selected = FindQueueByFullName(queues, printerName);
                    if (selected != null)
                        return selected;

                    // Short names are supported only for legacy saved settings.
                    // An UNC target must never be reduced to a short name: that
                    // could select a similarly named local/USB queue instead.
                    if (!printerName.TrimStart().StartsWith(@"\\", StringComparison.Ordinal))
                    {
                        var matches = FindQueuesByShortName(queues, printerName);
                        if (matches.Count == 1)
                            return matches[0];
                    }

                    return null;
                }

                return server.DefaultPrintQueue;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintQueueManager] ResolvePrintQueue failed: {ex.Message}");
                // An explicit printer selection must never fall back to the
                // Windows default (often a physically connected USB printer).
                // Returning null lets the UI report that the selected queue was
                // not resolved instead of silently sending to another device.
                if (!string.IsNullOrWhiteSpace(printerName))
                    return null;

                try
                {
                    var server = new LocalPrintServer();
                    return server.DefaultPrintQueue;
                }
                catch { return null; }
            }
        }

        private static string ReadQueueFullName(PrintQueue queue)
        {
            try { return queue.FullName ?? string.Empty; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintQueueManager] Queue FullName read failed: {ex.Message}");
                return string.Empty;
            }
        }

        private static void DisposeQueues(IEnumerable<PrintQueue> queues)
        {
            foreach (var queue in queues)
            {
                try { queue.Dispose(); }
                catch (Exception ex) { Debug.WriteLine($"[PrintQueueManager] Queue dispose failed: {ex.Message}"); }
            }
        }

        private static PrintQueue? FindQueueByFullName(
            IEnumerable<PrintQueue> queues,
            string printerName)
        {
            foreach (var queue in queues)
            {
                try
                {
                    if (string.Equals(queue.FullName, printerName, StringComparison.OrdinalIgnoreCase))
                        return queue;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PrintQueueManager] Queue identity read failed: {ex.Message}");
                }
            }

            return null;
        }

        private static List<PrintQueue> FindQueuesByShortName(
            IEnumerable<PrintQueue> queues,
            string printerName)
        {
            var matches = new List<PrintQueue>();
            foreach (var queue in queues)
            {
                try
                {
                    if (string.Equals(queue.Name, printerName, StringComparison.OrdinalIgnoreCase))
                        matches.Add(queue);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PrintQueueManager] Queue name read failed: {ex.Message}");
                }
            }

            return matches;
        }
    }
}
