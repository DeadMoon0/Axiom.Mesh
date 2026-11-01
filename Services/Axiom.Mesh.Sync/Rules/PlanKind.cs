namespace Axiom.Mesh.Sync;

/// <summary>What a run does (<see cref="SyncPlanner.Plan"/>, SYNC-SERVICE.md §3).</summary>
internal enum PlanKind
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>The directory and the branch agree: nothing to write anywhere.</summary>
    Nothing = 1,

    /// <summary>The directory changed and the branch did not: publish a Version naming the base leaves.</summary>
    Publish = 2,

    /// <summary>The branch moved to one new leaf and the directory did not change: write that leaf's Tree.</summary>
    Pull = 3,

    /// <summary>The branch has several leaves and the directory did not change: merge them two at a time, lowest first, and write the result.</summary>
    MergeChain = 4,

    /// <summary>Both changed: publish the directory as a Version naming the base leaves, then merge the leaves.</summary>
    PublishThenMerge = 5,

    /// <summary>No base this binding knows (new, or its leaves are gone): merge the directory against the empty tree as a virtual leaf with the branch, and publish the merge, never over the leaf.</summary>
    FirstSync = 6,

    /// <summary>A read-only binding: write what the branch holds (its leaves merged without writing a Version), never a file changed here.</summary>
    PullReadOnly = 7,
}
