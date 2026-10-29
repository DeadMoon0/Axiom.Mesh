using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Core;

/// <summary>
/// Where replication stands on a node (SPEC.md §10): the wants outstanding and waiting, the counters, and each established
/// session's traffic. A snapshot; read <see cref="MeshNode.Replication"/> again for news.
/// </summary>
public sealed class ReplicationStatus
{
    /// <summary>Hashes asked of a peer and not yet answered.</summary>
    public required int WantsOutstanding { get; init; }

    /// <summary>Hashes wanted that no connected peer is known to hold; they wait for a <c>Have</c> or a listing.</summary>
    public required int WantsWaiting { get; init; }

    /// <summary>Objects received from peers since the node started.</summary>
    public required long ObjectsIn { get; init; }

    /// <summary>Objects sent to peers since the node started.</summary>
    public required long ObjectsOut { get; init; }

    /// <summary>Entries stored since the node started.</summary>
    public required long EntriesStored { get; init; }

    /// <summary>Protocol violations of peers since the node started.</summary>
    public required long Violations { get; init; }

    /// <summary>Bytes in the held buffer (SPEC.md §10.6).</summary>
    public required long HeldBytes { get; init; }

    /// <summary>The established sessions.</summary>
    public required IReadOnlyList<SessionTraffic> Sessions { get; init; }

    /// <summary>
    /// Whether the node has nothing in flight: no command for the store, no change set unhandled, no want that can be
    /// asked or is asked, and every session idle.
    /// </summary>
    public required bool IsIdle { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        $"wants {WantsOutstanding} outstanding, {WantsWaiting} waiting; objects in {ObjectsIn} out {ObjectsOut}; entries {EntriesStored}{(IsIdle ? "; idle" : string.Empty)}; "
        + string.Join(" | ", Sessions.Select(s => s.ToString()));
}
