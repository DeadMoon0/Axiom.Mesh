using System.Collections.Generic;
using System.Linq;
using static Axiom.Mesh.Sync.Tests.Support.Facts;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// SYNC-SERVICE.md §3 step 7 with SPEC.md §9.3, §9.5 and LOCAL-SPEC.md §9.4: what a run does from the base the binding
/// remembers, the directory's state now and the branch's leaves. Parents are always named explicitly; a first sync never
/// publishes over a leaf; a read-only binding never publishes.
/// </summary>
public sealed class PlannerTests
{
    private static readonly ObjectHash Empty = TreeSet.EmptyTree.Hash;
    private static readonly ObjectHash BaseTree = Hash(0xb1);
    private static readonly ObjectHash Changed = Hash(0xc1);
    private static readonly VersionFact Root = Version(1, 0, Laptop, tree: BaseTree);

    [Fact]
    public void NothingChangedAnywhereIsNothing()
    {
        SyncPlan plan = SyncPlanner.Plan(Input(BaseTree, [Root], BaseTree, [Root]));

        Assert.Equal(PlanKind.Nothing, plan.Kind);
        Assert.False(plan.LocalChanges);
    }

    [Fact]
    public void ALocalChangeOnTheBaseLeavesIsPublishedNamingThem()
    {
        VersionFact other = Version(2, 0, Desktop);

        SyncPlan plan = SyncPlanner.Plan(Input(BaseTree, [Root, other], Changed, [Root, other]));

        Assert.Equal(PlanKind.Publish, plan.Kind);
        Assert.Equal(new[] { Root.Hash, other.Hash }.Order(), plan.Parents);
    }

    [Fact]
    public void ParentsAreAlwaysNamedExplicitly_NeverLeftToTheHost()
    {
        VersionFact beta = Version(3, 4, Desktop, "beta");
        PlanInput noMainYet = Input(null, [], Changed, [], beta);
        PlanInput nothingAtAll = Input(null, [], Changed, []);

        SyncPlan fork = SyncPlanner.Plan(noMainYet);
        SyncPlan first = SyncPlanner.Plan(nothingAtAll);

        Assert.Equal(PlanKind.Publish, fork.Kind);
        Assert.Equal([beta.Hash], fork.Parents);
        Assert.Equal((PlanKind.Publish, 0), (first.Kind, first.Parents.Count));
    }

    [Fact]
    public void AnEmptyDirectoryOnAnEmptyBranchIsNothing()
    {
        Assert.Equal(PlanKind.Nothing, SyncPlanner.Plan(Input(null, [], Empty, [])).Kind);
    }

    [Fact]
    public void ANewLeafOnTopOfTheBaseIsPulled()
    {
        VersionFact next = Version(2, 1, Desktop, parents: [Root]);

        SyncPlan plan = SyncPlanner.Plan(Input(BaseTree, [Root], BaseTree, [next]));

        Assert.Equal((PlanKind.Pull, next), (plan.Kind, plan.Target));
    }

    [Fact]
    public void SeveralNewLeavesAreMergedInAChain()
    {
        VersionFact a = Version(2, 1, Desktop, parents: [Root]);
        VersionFact b = Version(3, 1, Phone, parents: [Root]);

        SyncPlan plan = SyncPlanner.Plan(Input(BaseTree, [Root], BaseTree, [a, b]));

        Assert.Equal(PlanKind.MergeChain, plan.Kind);
        Assert.Equal(new[] { a, b }.OrderBy(l => l.Key), plan.Leaves);
    }

    [Fact]
    public void BothChangedPublishesNamingTheBaseLeaves_ThenMerges()
    {
        VersionFact remote = Version(2, 1, Desktop, parents: [Root]);

        SyncPlan plan = SyncPlanner.Plan(Input(BaseTree, [Root], Changed, [remote]));

        Assert.Equal(PlanKind.PublishThenMerge, plan.Kind);
        Assert.Equal([Root.Hash], plan.Parents);
        Assert.Equal([remote], plan.Leaves);
    }

    // A non-empty directory bound to an Archive that has a branch: merged against the empty tree with the branch, never a
    // Version of the directory alone written over the leaf.
    [Fact]
    public void AFirstSyncOfANonEmptyDirectoryMergesAgainstTheEmptyTree_NeverPublishesOverTheLeaf()
    {
        SyncPlan plan = SyncPlanner.Plan(Input(null, [], Changed, [Root]));

        Assert.Equal(PlanKind.FirstSync, plan.Kind);
        Assert.Empty(plan.Parents);
        Assert.Equal([Root], plan.Leaves);
    }

    [Fact]
    public void AFirstSyncOfAnEmptyDirectoryPullsTheBranch()
    {
        SyncPlan plan = SyncPlanner.Plan(Input(null, [], Empty, [Root]));

        Assert.Equal((PlanKind.Pull, Root), (plan.Kind, plan.Target));
    }

    // Another store epoch discards the Version DAG: the leaves the base was taken from are gone, and the binding starts over as
    // a first sync whose local files are its own.
    [Fact]
    public void ABaseWhoseLeavesAreGoneIsAFirstSync()
    {
        VersionFact rewritten = Version(9, 3, Desktop);

        SyncPlan plan = SyncPlanner.Plan(InputOfHashes(BaseTree, [Hash(0x77)], BaseTree, [rewritten]));

        Assert.Equal(PlanKind.FirstSync, plan.Kind);
    }

    [Fact]
    public void AReadOnlyBindingNeverPublishes_ItPullsAndReportsLocalChanges()
    {
        VersionFact remote = Version(2, 1, Desktop, parents: [Root]);

        SyncPlan pulled = SyncPlanner.Plan(Input(BaseTree, [Root], Changed, [remote]) with { ReadOnly = true });
        SyncPlan kept = SyncPlanner.Plan(Input(BaseTree, [Root], Changed, [Root]) with { ReadOnly = true });
        SyncPlan first = SyncPlanner.Plan(Input(null, [], Changed, [Root]) with { ReadOnly = true });

        Assert.Equal((PlanKind.PullReadOnly, true), (pulled.Kind, pulled.LocalChanges));
        Assert.Equal((PlanKind.Nothing, true), (kept.Kind, kept.LocalChanges));
        Assert.Equal((PlanKind.PullReadOnly, true), (first.Kind, first.LocalChanges));
    }

    private static PlanInput Input(ObjectHash? baseTree, IReadOnlyList<VersionFact> baseLeaves, ObjectHash local, IReadOnlyList<VersionFact> leaves, params VersionFact[] others) =>
        new(false, baseTree, [.. baseLeaves.Select(l => l.Hash)], local, Empty, [.. leaves.OrderBy(l => l.Key)], Dag([.. baseLeaves.Concat(leaves).Concat(others).DistinctBy(v => v.Hash)]));

    private static PlanInput InputOfHashes(ObjectHash? baseTree, IReadOnlyList<ObjectHash> baseLeaves, ObjectHash local, IReadOnlyList<VersionFact> leaves) =>
        new(false, baseTree, baseLeaves, local, Empty, leaves, Dag([.. leaves]));
}
