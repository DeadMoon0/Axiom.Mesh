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

    /// <summary>A spawned service's child started (LOCAL-SPEC.md §4.2); its token is never logged.</summary>
    /// <param name="logger">The daemon's logger.</param>
    /// <param name="name">The service.</param>
    /// <param name="pid">The child's process id.</param>
    /// <param name="executable">Its executable.</param>
    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "service {Name} started: pid {Pid}, \"{Executable}\"")]
    public static partial void ServiceStarted(ILogger logger, string name, int pid, string executable);

    /// <summary>A line a spawned service wrote to its standard output or error: its log.</summary>
    /// <param name="logger">The daemon's logger.</param>
    /// <param name="name">The service.</param>
    /// <param name="line">The line.</param>
    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "service {Name}: {Line}")]
    public static partial void ServiceOutput(ILogger logger, string name, string line);

    /// <summary>A spawned service's child exited, and what the supervisor does about it.</summary>
    /// <param name="logger">The daemon's logger.</param>
    /// <param name="name">The service.</param>
    /// <param name="exitCode">Its exit code; -1 when it did not start.</param>
    /// <param name="decision">The supervisor's decision, in words.</param>
    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "service {Name} exited with {ExitCode}: {Decision}")]
    public static partial void ServiceExited(ILogger logger, string name, int exitCode, string decision);

    /// <summary>The supervisor gave a spawned service up until the daemon restarts; never silent.</summary>
    /// <param name="logger">The daemon's logger.</param>
    /// <param name="name">The service.</param>
    /// <param name="why">Why.</param>
    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "service {Name} is given up and stays stopped: {Why}")]
    public static partial void ServiceGaveUp(ILogger logger, string name, string why);

    /// <summary>A spawned service did not exit within the stop grace and was killed with every process it started.</summary>
    /// <param name="logger">The daemon's logger.</param>
    /// <param name="name">The service.</param>
    /// <param name="grace">The grace, in milliseconds.</param>
    [LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "service {Name} did not exit within {Grace} ms of the stop and is killed")]
    public static partial void ServiceKilled(ILogger logger, string name, double grace);

    /// <summary>A spawned service's child could not be started.</summary>
    /// <param name="logger">The daemon's logger.</param>
    /// <param name="name">The service.</param>
    /// <param name="executable">Its executable.</param>
    /// <param name="why">The system's message.</param>
    [LoggerMessage(EventId = 12, Level = LogLevel.Error, Message = "service {Name} could not be started from \"{Executable}\": {Why}")]
    public static partial void ServiceNotStarted(ILogger logger, string name, string executable, string why);
}
