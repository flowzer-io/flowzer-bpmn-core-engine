using Microsoft.Extensions.Logging;

namespace WebApiEngine.Tests;

/// <summary>Ein kleiner Logger-Testdoppel, der die formatierte Lognachricht festhält.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<CapturedLogEntry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception)));
    }

    internal sealed record CapturedLogEntry(LogLevel Level, string Message);

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
