using Axiom.Mesh.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Host;

/// <summary>The daemon's own log messages, source-generated.</summary>
internal static partial class HostLog
{
    /// <summary>The daemon started and says where everything is.</summary>
    /// <param name="logger">The daemon's logger.</param>
    /// <param name="version">Its version.</param>
    /// <param name="home">Its home.</param>
    /// <param name="pipe">Its pipe.</param>
    /// <param name="mesh">Its mesh endpoint.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "axiom-meshd {Version} runs: home {Home}, pipe {Pipe}, mesh {Mesh}")]
    public static partial void Started(ILogger logger, string version, string home, string pipe, string mesh);

    /// <summary>A pipe connection was refused before <c>Hello</c> (LOCAL-SPEC.md §2.2).</summary>
    /// <param name="logger">The daemon's logger.</param>
    /// <param name="pid">The peer's process id.</param>
    /// <param name="executable">Its executable.</param>
    /// <param name="user">Its user.</param>
    /// <param name="reason">Why.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "pipe connection from pid {Pid} \"{Executable}\" as \"{User}\" refused: {Reason}")]
    public static partial void PipeRefused(ILogger logger, int pid, string executable, string user, string reason);

    /// <summary>A note about the configuration that does not stop the start.</summary>
    /// <param name="logger">The daemon's logger.</param>
    /// <param name="note">The note.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "configuration: {Note}")]
    public static partial void ConfigurationNote(ILogger logger, string note);

    /// <summary>What discovery reported (SPEC.md §3).</summary>
    /// <param name="logger">The daemon's logger.</param>
    /// <param name="fact">The fact.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "discovery: {Fact}")]
    public static partial void Discovery(ILogger logger, string fact);

    /// <summary>The daemon stops.</summary>
    /// <param name="logger">The daemon's logger.</param>
    /// <param name="grace">The local clients' grace.</param>
    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "axiom-meshd stops: local clients get {Grace} ms to finish")]
    public static partial void Stopping(ILogger logger, double grace);

    /// <summary>
    /// How renames in the store directory are made durable on this platform (SPEC.md §11.1): <c>NotNeeded</c> on Windows,
    /// <c>Flushed</c> where the directory was <c>fsync</c>ed, <c>Unsupported</c> where it could not be. The install-script
    /// job reads this line on Linux (STRUCTURAL-DEBT.md 14).
    /// </summary>
    /// <param name="logger">The daemon's logger.</param>
    /// <param name="data">The store directory.</param>
    /// <param name="flush">What a directory flush does here.</param>
    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "store {Data}: directory flush {Flush}")]
    public static partial void StoreFlush(ILogger logger, string data, StoreDirectoryFlush flush);
}
