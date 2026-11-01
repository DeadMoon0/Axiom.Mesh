using System.Linq;
using Axiom.Mesh.Sync.Tests.Support;
using static Axiom.Mesh.Sync.Tests.Support.Facts;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// SPEC.md §9.5: the leaves of a branch are the stored Versions with its label from which no other stored Version with its label
/// descends; its head for display is the leaf highest in the total order (clock, device, hash; §9.3).
/// </summary>
public sealed class LeafTests
{
    [Fact]
    public void ALinearHistoryHasOneLeaf_ItsLatestVersion()
    {
        VersionFact first = Version(1, 0, Laptop);
        VersionFact second = Version(2, 1, Desktop, parents: [first]);
        VersionFact third = Version(3, 2, Laptop, parents: [second]);

        Assert.Equal([third], Leaves.Of(Dag(first, second, third), "main"));
    }

    [Fact]
    public void TwoVersionsOnTheSameParentAreTwoLeaves_AscendingInTheTotalOrder_TheHeadIsTheHigher()
    {
        VersionFact root = Version(1, 0, Laptop);
        VersionFact fromLaptop = Version(2, 1, Laptop, parents: [root]);
        VersionFact fromDesktop = Version(3, 1, Desktop, parents: [root]);

        var leaves = Leaves.Of(Dag(root, fromDesktop, fromLaptop), "main");

        Assert.Equal([fromLaptop, fromDesktop], leaves);
        Assert.Equal(fromDesktop, Leaves.Head(leaves));
    }

    [Fact]
    public void AMergeNamingBothLeavesLeavesOneLeaf()
    {
        VersionFact root = Version(1, 0, Laptop);
        VersionFact a = Version(2, 1, Laptop, parents: [root]);
        VersionFact b = Version(3, 1, Desktop, parents: [root]);
        VersionFact merge = Version(4, 2, Desktop, parents: [a, b]);

        Assert.Equal([merge], Leaves.Of(Dag(root, a, b, merge), "main"));
    }

    // A Version of another branch is no leaf of main; a main Version written on top of it descends from the main Version
    // below it through the other branch, so that one is no leaf either.
    [Fact]
    public void AnotherBranchIsNoLeaf_AndDescentRunsThroughIt()
    {
        VersionFact root = Version(1, 0, Laptop);
        VersionFact beta = Version(2, 1, Laptop, "beta", parents: [root]);
        VersionFact back = Version(3, 2, Desktop, parents: [beta]);

        Assert.Equal([back], Leaves.Of(Dag(root, beta, back), "main"));
        Assert.Equal([beta], Leaves.Of(Dag(root, beta, back), "beta"));
        Assert.Empty(Leaves.Of(Dag(root, beta, back), "gamma"));
        Assert.Null(Leaves.Head(Leaves.Of(Dag(root, beta, back), "gamma").ToList()));
    }
}
