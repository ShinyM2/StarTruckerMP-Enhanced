using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace StarTruckMP.Server.Server.Services;

/// <summary>
/// Keeps the last few hundred log lines in memory so the admin page can show them. Everything
/// still goes to the console as before; this only listens in.
/// </summary>
public sealed class MemoryLogSink : ILoggerProvider
{
    private const int Limit = 300;

    public static readonly MemoryLogSink Instance = new();

    private readonly ConcurrentQueue<string> _lines = new();

    public string[] Lines() => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new Listener(this, categoryName);

    public void Dispose() { }

    private void Add(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > Limit && _lines.TryDequeue(out _)) { }
    }

    private sealed class Listener(MemoryLogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var shortCategory = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
            var line = $"{DateTime.Now:HH:mm:ss} {Level(logLevel)} {shortCategory}: {formatter(state, exception)}";
            if (exception is not null) line += " — " + exception.Message;
            sink.Add(line);
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERR ",
            LogLevel.Critical => "CRIT",
            _ => "INFO"
        };
    }
}
