namespace MosquitoNetCalculator.Models
{
    /// <summary>Тип результата отправки задания в очередь печати.</summary>
    public enum PrintResultType
    {
        Success,
        PrinterOffline,
        PrinterOutOfPaper,
        PrinterTonerLow,
        PrinterError,
        SpoolerStopped,
        AccessDenied,
        QueueError,
        Unknown
    }

    /// <summary>Результат попытки отправки задания в очередь печати.</summary>
    public readonly struct PrintResult
    {
        public PrintResultType Type { get; init; }
        public string UserMessage { get; init; }
        public string? DebugMessage { get; init; }
        public bool IsRetryable { get; init; }

        public static PrintResult Ok() => new()
        {
            Type = PrintResultType.Success,
            UserMessage = "",
            IsRetryable = false
        };
    }
}
