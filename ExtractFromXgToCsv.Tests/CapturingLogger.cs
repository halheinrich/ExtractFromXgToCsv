using Microsoft.Extensions.Logging;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Minimal <see cref="ILogger{T}"/> that captures each entry's level, fully
/// formatted message and exception — enough to assert what a component logged,
/// or that it logged nothing of a kind.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>Every entry logged, in order.</summary>
    public List<LogEntry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>One entry captured by <see cref="CapturingLogger{T}"/>.</summary>
internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
