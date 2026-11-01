using System;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Service;

/// <summary>The runtime's log messages, source-generated. A spawned service's log reaches the Host's log through its standard error.</summary>
internal static partial class ServiceLog
{
    [LoggerMessage(EventId = 201, Level = LogLevel.Error, Message = "service {Name} cannot run: {Problems}")]
    public static partial void Unusable(ILogger logger, string name, string problems);

    [LoggerMessage(EventId = 202, Level = LogLevel.Error, Message = "service {Name} has no token: set {Variable} (the Host does for a service it spawns) or list it as external in {ConfigFile}")]
    public static partial void NoToken(ILogger logger, string name, string variable, string configFile);

    [LoggerMessage(EventId = 203, Level = LogLevel.Error, Message = "the service's state in \"{Directory}\" cannot be opened")]
    public static partial void StateFailed(ILogger logger, string directory, Exception exception);

    [LoggerMessage(EventId = 204, Level = LogLevel.Warning, Message = "not connected to the Host at {Host}: {Why}")]
    public static partial void NotConnected(ILogger logger, string host, string why);

    [LoggerMessage(EventId = 205, Level = LogLevel.Error, Message = "the Host refused service {Name} for good ({Detail}); exiting")]
    public static partial void Refused(ILogger logger, string name, string detail);

    [LoggerMessage(EventId = 206, Level = LogLevel.Information, Message = "connecting again in {Seconds} s")]
    public static partial void Reconnecting(ILogger logger, double seconds);

    [LoggerMessage(EventId = 207, Level = LogLevel.Information, Message = "service {Name} connected to {Host}; catching up from store sequence {FromSeq}, discarding the index: {Discard}")]
    public static partial void Connected(ILogger logger, string name, string host, ulong fromSeq, bool discard);

    [LoggerMessage(EventId = 208, Level = LogLevel.Information, Message = "the Host drains; the connection ends with Bye")]
    public static partial void Drained(ILogger logger);

    [LoggerMessage(EventId = 209, Level = LogLevel.Warning, Message = "the connection ended: {Why}")]
    public static partial void Lost(ILogger logger, string why);

    [LoggerMessage(EventId = 210, Level = LogLevel.Error, Message = "service {Name} failed while handling the Host's events; exiting so that it is restarted")]
    public static partial void HandlerFailed(ILogger logger, string name, Exception exception);

    [LoggerMessage(EventId = 211, Level = LogLevel.Warning, Message = "RegisterKind {Kind} was answered {Status}: {Message}")]
    public static partial void KindRefused(ILogger logger, ushort kind, string status, string message);

    [LoggerMessage(EventId = 212, Level = LogLevel.Warning, Message = "the {Topic} subscription lapsed with {Dropped} events dropped; subscribing again")]
    public static partial void Lapsed(ILogger logger, string topic, uint dropped);
}
