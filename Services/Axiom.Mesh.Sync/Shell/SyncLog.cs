using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Sync;

/// <summary>The sync service's log messages, source-generated; a spawned service's log reaches the Host's log.</summary>
internal static partial class SyncLog
{
    [LoggerMessage(EventId = 401, Level = LogLevel.Information, Message = "sync: connected, {Bindings} bindings, {Versions}")]
    public static partial void Connected(ILogger logger, int bindings, string versions);

    [LoggerMessage(EventId = 402, Level = LogLevel.Information, Message = "sync: another store epoch; the Version DAG is discarded and every binding starts over as a first sync")]
    public static partial void EpochChanged(ILogger logger);

    [LoggerMessage(EventId = 403, Level = LogLevel.Information, Message = "sync {Directory}: {Plan}, ended {State}")]
    public static partial void RunEnded(ILogger logger, string directory, string plan, BindingState state);

    [LoggerMessage(EventId = 404, Level = LogLevel.Warning, Message = "sync {Directory}: the run failed: {Problem}")]
    public static partial void RunFailed(ILogger logger, string directory, string problem);

    [LoggerMessage(EventId = 405, Level = LogLevel.Information, Message = "sync {Directory}: the system refused to watch it; only the rescan every {Minutes} minutes finds changes")]
    public static partial void NotWatched(ILogger logger, string directory, double minutes);

    [LoggerMessage(EventId = 406, Level = LogLevel.Information, Message = "sync event {Event} not emitted: {Why}")]
    public static partial void NotEmitted(ILogger logger, string @event, string why);
}
