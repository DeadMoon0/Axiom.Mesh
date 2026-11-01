using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Host.Tests.Support;

/// <summary>Every line the daemon logs, kept for the test (<c>HostStart.AlsoLogTo</c>).</summary>
internal sealed class CapturedLog : ILoggerProvider
{
    private readonly ConcurrentQueue<string> lines = new();

    /// <summary>The lines so far.</summary>
    public string[] Lines => [.. lines];

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
        // Nothing to release.
    }

    /// <summary>Waits until a line contains <paramref name="text"/>, within 30 seconds of real time.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The first such line.</returns>
    public async Task<string> WaitForAsync(string text)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            if (lines.FirstOrDefault(l => l.Contains(text, StringComparison.Ordinal)) is { } line)
                return line;
            if (deadline.IsCancellationRequested)
                throw new TimeoutException($"No log line contained \"{text}\" within 30 s. The log: {string.Join(" | ", lines.TakeLast(20))}");
            await Task.Yield();
        }
    }

    private sealed class Logger(CapturedLog owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.lines.Enqueue($"{logLevel} {eventId.Id} {formatter(state, exception)}");
    }
}
