using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Service;

/// <summary>
/// A service's log as single lines on one writer (standard error): the level, the category and the message, an
/// exception's type and message after it. The Host pumps a spawned child's standard error into its own log, so these lines
/// end up there; nothing here is meant for a person at a console (DESIGN.md §10.5).
/// </summary>
internal sealed class LineLoggerFactory : ILoggerFactory
{
    private readonly TextWriter writer;
    private readonly object gate = new();

    public LineLoggerFactory(TextWriter writer) => this.writer = TextWriter.Synchronized(writer);

    public ILogger CreateLogger(string categoryName) => new LineLogger(this, categoryName);

    public void AddProvider(ILoggerProvider provider)
    {
        // One sink only.
    }

    public void Dispose()
    {
        // The writer belongs to the process.
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        string line = exception is null ? $"{Short(level)} {category}: {message}" : $"{Short(level)} {category}: {message} ({exception.GetType().Name}: {exception.Message})";
        lock (gate)
            writer.WriteLine(line);
    }

    private static string Short(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        _ => "crit",
    };

    private sealed class LineLogger(LineLoggerFactory factory, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                factory.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
