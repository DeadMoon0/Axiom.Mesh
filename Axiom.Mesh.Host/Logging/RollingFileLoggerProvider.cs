using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Host;

/// <summary>
/// A small rolling file sink for the daemon's log (fundamentals plan §2.6): one line per message in
/// <c>axiom-meshd-&lt;date&gt;.log</c> under the log directory, a new file each day and whenever one passes 10 MiB, the
/// newest 10 files kept. Writes are serialized and flushed per line, so the log of a crash is complete up to the crash.
/// </summary>
internal sealed class RollingFileLoggerProvider : ILoggerProvider
{
    /// <summary>The size after which the sink starts a new file.</summary>
    public const long MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>How many files the sink keeps.</summary>
    public const int KeptFiles = 10;

    private readonly object gate = new();
    private readonly string directory;
    private readonly TimeProvider time;
    private StreamWriter? writer;
    private string? currentFile;

    /// <summary>A sink writing into a directory, made private if it does not exist.</summary>
    /// <param name="directory">The log directory.</param>
    /// <param name="time">The clock the lines are stamped with.</param>
    public RollingFileLoggerProvider(string directory, TimeProvider time)
    {
        this.directory = directory;
        this.time = time;
        HostFiles.CreatePrivateDirectory(directory);
    }

    /// <summary>The file written to now; <see langword="null"/> before the first line.</summary>
    public string? CurrentFile
    {
        get
        {
            lock (gate)
                return currentFile;
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            writer?.Dispose();
            writer = null;
        }
    }

    /// <summary>The line a message becomes: time, level, category, message, and the exception's first line.</summary>
    /// <param name="at">When.</param>
    /// <param name="level">The level.</param>
    /// <param name="category">The logger's category.</param>
    /// <param name="message">The formatted message.</param>
    /// <param name="exception">An exception, or <see langword="null"/>.</param>
    /// <returns>The line, without a line break.</returns>
    public static string Line(DateTimeOffset at, LogLevel level, string category, string message, Exception? exception)
    {
        var line = new StringBuilder(64 + message.Length);
        line.Append(at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            .Append(' ').Append(Level(level))
            .Append(' ').Append(category)
            .Append(": ").Append(message.ReplaceLineEndings(" "));
        if (exception is not null)
            line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message.ReplaceLineEndings(" "));
        return line.ToString();
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        DateTimeOffset now = time.GetUtcNow();
        string line = Line(now, level, category, message, exception);
        lock (gate)
        {
            try
            {
                StreamWriter target = WriterFor(now);
                target.WriteLine(line);
                target.Flush();
            }
            catch (IOException)
            {
                // A log that cannot be written must not stop the daemon; the console still has the line.
            }
        }
    }

    private StreamWriter WriterFor(DateTimeOffset now)
    {
        string day = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        bool rotate = writer is null || currentFile is null || !Path.GetFileName(currentFile).StartsWith("axiom-meshd-" + day, StringComparison.Ordinal)
            || writer.BaseStream.Length >= MaxFileBytes;
        if (!rotate)
            return writer!;

        writer?.Dispose();
        int index = 0;
        string file;
        do
            file = Path.Combine(directory, index == 0 ? $"axiom-meshd-{day}.log" : $"axiom-meshd-{day}.{index}.log");
        while (File.Exists(file) && new FileInfo(file).Length >= MaxFileBytes && ++index > 0);
        var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = HostFiles.PrivateFile;
        writer = new StreamWriter(new FileStream(file, options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        currentFile = file;
        Prune();
        return writer;
    }

    private void Prune()
    {
        foreach (FileInfo old in new DirectoryInfo(directory).GetFiles("axiom-meshd-*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeptFiles))
        {
            try
            {
                old.Delete();
            }
            catch (IOException)
            {
                // Kept for now; the next roll tries again.
            }
        }
    }

    private static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        LogLevel.Critical => "crit",
        _ => "none",
    };

    private sealed class FileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
