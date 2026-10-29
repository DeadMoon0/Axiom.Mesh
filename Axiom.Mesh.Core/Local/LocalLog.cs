using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Core;

/// <summary>The local port's log messages, source-generated (LOCAL-SPEC.md §2.2, §11.3, §13).</summary>
internal static partial class LocalLog
{
    /// <summary>
    /// A user-key request (§2.2, §13): logged with the process id and the executable of its connection, whether it
    /// succeeded or not.
    /// </summary>
    /// <param name="logger">The port's logger.</param>
    /// <param name="request">The request's name.</param>
    /// <param name="id">Its message id.</param>
    /// <param name="pid">The connection's process id.</param>
    /// <param name="executable">The connection's executable.</param>
    /// <param name="user">The connection's operating-system user.</param>
    /// <param name="connection">The connection id.</param>
    /// <param name="status">How it was answered.</param>
    [LoggerMessage(EventId = 102, Level = LogLevel.Warning, Message = "user-key request {Request} ({Id}) from pid {Pid} \"{Executable}\" as \"{User}\" on local connection #{Connection}: {Status}")]
    public static partial void UserKeyRequest(ILogger logger, string request, ushort id, int pid, string executable, string user, long connection, string status);

    /// <summary>A handler failed in a way no status describes; the request was answered and the connection kept.</summary>
    /// <param name="logger">The port's logger.</param>
    /// <param name="request">The request's name.</param>
    /// <param name="connection">The connection id.</param>
    /// <param name="exception">What failed.</param>
    [LoggerMessage(EventId = 104, Level = LogLevel.Error, Message = "local request {Request} on connection #{Connection} failed")]
    public static partial void HandlerFailed(ILogger logger, string request, long connection, System.Exception exception);
}
