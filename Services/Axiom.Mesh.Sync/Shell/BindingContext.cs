using System.Threading;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Sync;

/// <summary>What every binding's worker shares: the database, the Version DAG, the options, the connection and the gate.</summary>
/// <param name="Store">The database.</param>
/// <param name="Versions">The Version DAG.</param>
/// <param name="Options">The clock, the stability time, the seams.</param>
/// <param name="Sessions">The connection, while there is one.</param>
/// <param name="Gate">At most this many runs at once over all bindings.</param>
/// <param name="Log">The service's log.</param>
internal sealed record BindingContext(SyncStore Store, VersionIndex Versions, SyncServiceOptions Options, SessionHolder Sessions, SemaphoreSlim Gate, ILogger Log)
{
    /// <summary>How many runs go at once over all bindings.</summary>
    public const int RunsAtOnce = 4;
}
