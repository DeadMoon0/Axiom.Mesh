using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Chat;

/// <summary>The chat service's log messages, source-generated; a spawned service's log reaches the Host's log.</summary>
internal static partial class ChatLog
{
    [LoggerMessage(EventId = 301, Level = LogLevel.Information, Message = "chat index: {Action} (schema {Schema}, last seq {LastSeq}, resuming at {FromSeq})")]
    public static partial void Connected(ILogger logger, IndexAction action, int schema, ulong lastSeq, ulong fromSeq);

    [LoggerMessage(EventId = 302, Level = LogLevel.Information, Message = "chat event {Event} not emitted: {Why}")]
    public static partial void NotEmitted(ILogger logger, string @event, string why);

    [LoggerMessage(EventId = 303, Level = LogLevel.Information, Message = "chat released {Count} pins of attachments no message named")]
    public static partial void PendingReleased(ILogger logger, int count);

    [LoggerMessage(EventId = 304, Level = LogLevel.Information, Message = "chat job {Job} ({Kind}) ended {State}: {Problem}")]
    public static partial void JobEnded(ILogger logger, uint job, JobKind kind, JobState state, string problem);
}
