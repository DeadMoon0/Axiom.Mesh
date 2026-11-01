using static Axiom.Mesh.Sync.Tests.Support.Facts;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// SPEC.md §9.5: the common ancestor of two leaves is the Version highest in the total order among those reachable from both;
/// the empty tree when none is.
/// </summary>
public sealed class AncestorTests
{
    [Fact]
    public void TheCommonAncestorIsTheHighestVersionReachableFromBoth()
    {
        VersionFact root = Version(1, 0, Laptop);
        VersionFact shared = Version(2, 1, Desktop, parents: [root]);
        VersionFact a = Version(3, 2, Laptop, parents: [shared]);
        VersionFact b = Version(4, 2, Desktop, parents: [shared]);
        VersionFact b2 = Version(5, 3, Desktop, parents: [b]);

        Assert.Equal(shared, Ancestor.Of(Dag(root, shared, a, b, b2), a, b2));
        Assert.Equal(shared, Ancestor.Of(Dag(root, shared, a, b, b2), b2, a));
    }

    [Fact]
    public void LeavesWithoutSharedHistoryHaveNone_TheMergeRunsAgainstTheEmptyTree()
    {
        VersionFact a = Version(1, 0, Laptop);
        VersionFact b = Version(2, 0, Desktop);

        Assert.Null(Ancestor.Of(Dag(a, b), a, b));
    }

    // A criss-cross: two merges each naming both of two earlier leaves; their common ancestors are both earlier leaves, neither
    // reachable from the other, and the higher in the total order is taken (no recursive merge).
    [Fact]
    public void InACrissCrossTheHighestOfTheCommonAncestorsIsTaken()
    {
        VersionFact root = Version(1, 0, Laptop);
        VersionFact x = Version(2, 1, Laptop, parents: [root]);
        VersionFact y = Version(3, 1, Desktop, parents: [root]);
        VersionFact m1 = Version(4, 2, Laptop, parents: [x, y]);
        VersionFact m2 = Version(5, 2, Desktop, parents: [x, y]);

        Assert.Equal(y, Ancestor.Of(Dag(root, x, y, m1, m2), m1, m2));
    }
}
