using Microsoft.Extensions.Logging;

namespace TestHistoryData.TestUtilities;

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, EventId EventId, string Message, Exception? Exception)> Entries { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, eventId, formatter(state, exception), exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
