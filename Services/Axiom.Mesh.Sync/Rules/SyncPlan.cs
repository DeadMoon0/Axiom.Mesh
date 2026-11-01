using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Sync;

/// <summary>
/// What a run does (SYNC-SERVICE.md §3), as <see cref="SyncPlanner.Plan"/> decided it: the kind, the parents a publish names
/// (always explicit, never left to the Host, LOCAL-SPEC.md §9.4), the leaf a pull writes, the leaves a merge resolves, and whether
/// the directory holds changes the binding will not publish. Made only by the planner.
/// </summary>
internal sealed class SyncPlan
{
    private SyncPlan(PlanKind kind, IReadOnlyList<ObjectHash> parents, VersionFact? target, IReadOnlyList<VersionFact> leaves, bool localChanges)
    {
        Kind = kind;
        Parents = parents;
        Target = target;
        Leaves = leaves;
        LocalChanges = localChanges;
    }

    /// <summary>What the run does.</summary>
    public PlanKind Kind { get; }

    /// <summary>For a publish: the parents, ascending as byte strings; empty only when the Archive holds no Version at all.</summary>
    public IReadOnlyList<ObjectHash> Parents { get; }

    /// <summary>For a pull: the one leaf whose Tree is written.</summary>
    public VersionFact? Target { get; }

    /// <summary>The branch's leaves, ascending in the total order, for a merge or a first sync.</summary>
    public IReadOnlyList<VersionFact> Leaves { get; }

    /// <summary>Whether the directory holds changes this run does not publish (a read-only binding).</summary>
    public bool LocalChanges { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}{(Parents.Count > 0 ? $" parents {Parents.Count}" : string.Empty)}{(LocalChanges ? ", local changes kept" : string.Empty)}";

    internal static SyncPlan Nothing(bool localChanges) => new(PlanKind.Nothing, [], null, [], localChanges);

    internal static SyncPlan Publish(IEnumerable<ObjectHash> parents) => new(PlanKind.Publish, Ascending(parents), null, [], false);

    internal static SyncPlan Pull(VersionFact leaf) => new(PlanKind.Pull, [], leaf, [leaf], false);

    internal static SyncPlan MergeChain(IReadOnlyList<VersionFact> leaves) => new(PlanKind.MergeChain, [], null, leaves, false);

    internal static SyncPlan PublishThenMerge(IEnumerable<ObjectHash> parents, IReadOnlyList<VersionFact> leaves) => new(PlanKind.PublishThenMerge, Ascending(parents), null, leaves, false);

    internal static SyncPlan FirstSync(IReadOnlyList<VersionFact> leaves) => new(PlanKind.FirstSync, [], null, leaves, false);

    internal static SyncPlan PullReadOnly(IReadOnlyList<VersionFact> leaves, bool localChanges) => new(PlanKind.PullReadOnly, [], null, leaves, localChanges);

    private static ObjectHash[] Ascending(IEnumerable<ObjectHash> parents) => [.. parents.Distinct().Order()];
}
