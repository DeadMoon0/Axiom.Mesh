using System;
using System.Linq;

namespace Axiom.Mesh.Sync;

/// <summary>
/// What a run does (SYNC-SERVICE.md §3 step 7), from the base the binding remembers, the directory's state now and the branch's
/// leaves:
/// <list type="bullet">
/// <item>the branch has no Version: publish the directory if it holds anything, naming the Archive's highest Version of any
/// branch as parent when there is one (a fork, SPEC.md §9.3);</item>
/// <item>no base, or a base whose leaves are no longer stored (a new binding, another store epoch): a first sync, which merges the
/// directory against the empty tree with the branch and publishes the merge naming the leaf, never over it;</item>
/// <item>the leaves are the base's: publish the directory's changes naming them, or nothing;</item>
/// <item>the leaves moved: pull the one new leaf, or merge them, after publishing the directory's changes naming the base's
/// leaves.</item>
/// </list>
/// A read-only binding never publishes: it writes what the branch holds and keeps every local change. Parents are always named
/// explicitly. Pure.
/// </summary>
internal static class SyncPlanner
{
    /// <summary>Decides a run.</summary>
    /// <param name="input">The base, the directory and the branch.</param>
    /// <returns>The plan.</returns>
    public static SyncPlan Plan(PlanInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        bool localChanged = input.BaseTree is { } baseTree ? input.LocalTree != baseTree : input.LocalTree != input.EmptyTree;
        if (input.Leaves.Count == 0)
        {
            if (!localChanged || input.LocalTree == input.EmptyTree || input.ReadOnly)
                return SyncPlan.Nothing(localChanged && input.ReadOnly);
            VersionFact? highest = input.Versions.Values.MaxBy(v => v.Key);
            return SyncPlan.Publish(highest is null ? [] : [highest.Hash]);
        }

        bool baseKnown = input.BaseLeaves.Count > 0 && input.BaseLeaves.All(input.Versions.ContainsKey);
        if (!baseKnown)
        {
            if (input.ReadOnly)
                return SyncPlan.PullReadOnly(input.Leaves, input.LocalTree != input.EmptyTree);
            if (input.LocalTree != input.EmptyTree)
                return SyncPlan.FirstSync(input.Leaves);
            return input.Leaves.Count == 1 ? SyncPlan.Pull(input.Leaves[0]) : SyncPlan.MergeChain(input.Leaves);
        }

        bool leavesAreTheBase = input.Leaves.Count == input.BaseLeaves.Count && input.Leaves.All(l => input.BaseLeaves.Contains(l.Hash));
        if (leavesAreTheBase)
        {
            if (!localChanged || input.ReadOnly)
                return SyncPlan.Nothing(localChanged);
            return SyncPlan.Publish(input.BaseLeaves);
        }

        if (input.ReadOnly)
            return SyncPlan.PullReadOnly(input.Leaves, localChanged);
        if (localChanged)
            return SyncPlan.PublishThenMerge(input.BaseLeaves, input.Leaves);
        return input.Leaves.Count == 1 ? SyncPlan.Pull(input.Leaves[0]) : SyncPlan.MergeChain(input.Leaves);
    }
}
