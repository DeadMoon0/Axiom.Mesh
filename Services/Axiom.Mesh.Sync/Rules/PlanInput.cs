using System.Collections.Generic;

namespace Axiom.Mesh.Sync;

/// <summary>What <see cref="SyncPlanner.Plan"/> decides on.</summary>
/// <param name="ReadOnly">The user may not write to the Archive: the binding pulls only.</param>
/// <param name="BaseTree">The Tree the directory last equalled; <see langword="null"/> before the first run.</param>
/// <param name="BaseLeaves">The leaves the base was taken from; empty before the first run and after a store epoch change.</param>
/// <param name="LocalTree">The directory's state now.</param>
/// <param name="EmptyTree">The empty Tree's hash.</param>
/// <param name="Leaves">The branch's leaves now, ascending in the total order.</param>
/// <param name="Versions">Every stored Version of the Archive, by hash.</param>
internal sealed record PlanInput(
    bool ReadOnly,
    ObjectHash? BaseTree,
    IReadOnlyList<ObjectHash> BaseLeaves,
    ObjectHash LocalTree,
    ObjectHash EmptyTree,
    IReadOnlyList<VersionFact> Leaves,
    IReadOnlyDictionary<ObjectHash, VersionFact> Versions);
