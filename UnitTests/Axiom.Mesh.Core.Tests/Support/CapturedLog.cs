using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>A logger factory that keeps every formatted message, for tests that read what a node logged.</summary>
internal sealed class CapturedLog : ILoggerFactory
{
    private readonly List<(string Category, LogLevel Level, string Message)> lines = [];

    /// <summary>Every message so far.</summary>
    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Lines
    {
        get
        {
            lock (lines)
                return [.. lines];
        }
    }

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private void Add(string category, LogLevel level, string message)
    {
        lock (lines)
            lines.Add((category, level, message));
    }

    private sealed class Logger(CapturedLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            log.Add(category, logLevel, formatter(state, exception));
    }
}
