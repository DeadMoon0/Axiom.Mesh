using System.Collections.Generic;
using System.Linq;
using static Axiom.Mesh.Sync.Tests.Support.Facts;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// SYNC-SERVICE.md §3 step 11 with SPEC.md §8.4 and §11.4: writing a target into a bound directory writes only what the target
/// changed and the directory still holds as the base had it; a file changed here is never overwritten or deleted by a pull.
/// </summary>
public sealed class DiskDiffTests
{
    [Fact]
    public void OnlyWhatTheTargetChangedIsWritten_WhatIsThereAlreadyIsLeft()
    {
        FlatTree baseTree = Folder(("same.txt", "s"), ("edited.txt", "v1"), ("resumed.txt", "r1"));
        FlatTree target = Folder(("same.txt", "s"), ("edited.txt", "v2"), ("resumed.txt", "r2"), ("new/one.txt", "1"));
        FlatTree local = Folder(("same.txt", "s"), ("edited.txt", "v1"), ("resumed.txt", "r2"));

        DiskPlan plan = DiskDiff.Of(baseTree, target, local, NoneBusy, windows: false);

        Assert.Equal(["edited.txt", "new/one.txt"], plan.Writes.Select(w => w.Key));
        Assert.Equal(["new"], plan.Directories);
        Assert.Empty(plan.Deletes);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void AFileChangedHereSinceTheBaseIsNeverOverwrittenOrDeletedByAPull()
    {
        FlatTree baseTree = Folder(("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"));
        FlatTree target = Folder(("a.txt", "a from the branch"), ("c.txt", "c from the branch"));
        FlatTree local = Folder(("a.txt", "a edited here"), ("b.txt", "b edited here"), ("c.txt", "c"));

        DiskPlan plan = DiskDiff.Of(baseTree, target, local, new HashSet<string> { "c.txt" }, windows: false);

        Assert.Empty(plan.Writes);
        Assert.Empty(plan.Deletes);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], plan.Skipped);
    }

    [Fact]
    public void DeletesComeDeepestFirst_ADirectoryOnlyOnceEmpty_AFileReplacesADirectoryOnlyWhenAllOfItGoes()
    {
        FlatTree baseTree = Folder(("old/deep/x.txt", "x"), ("old/y.txt", "y"), ("kept/z.txt", "z"), ("swap/w.txt", "w"));
        FlatTree target = Folder(("kept/z.txt", "z"), ("swap", "now a file"));
        FlatTree local = Folder(("old/deep/x.txt", "x"), ("old/y.txt", "y"), ("kept/z.txt", "z"), ("swap/w.txt", "w"), ("old/mine.txt", "mine"));

        DiskPlan plan = DiskDiff.Of(baseTree, target, local, NoneBusy, windows: false);

        Assert.Equal(["old/deep/x.txt", "swap/w.txt", "old/y.txt", "old/deep", "swap"], plan.Deletes);
        Assert.Equal(["swap"], plan.Writes.Select(w => w.Key));
        Assert.DoesNotContain("old", plan.Deletes);
    }

    [Fact]
    public void ANameTheFileSystemCannotHoldIsRefused_WithEverythingBelowIt()
    {
        FlatTree target = Folder(("a:b/c.txt", "c"), ("ok.txt", "o"), ("CON.txt", "con"), ("Readme", "1"), ("README", "2"));

        DiskPlan windows = DiskDiff.Of(FlatTree.Empty, target, FlatTree.Empty, NoneBusy, windows: true);
        DiskPlan linux = DiskDiff.Of(FlatTree.Empty, target, FlatTree.Empty, NoneBusy, windows: false);

        Assert.Equal(["CON.txt", "Readme", "a:b"], windows.Refused.Order(System.StringComparer.Ordinal));
        Assert.Equal(["README", "ok.txt"], windows.Writes.Select(w => w.Key));
        Assert.Empty(linux.Refused);
        Assert.Equal(5, linux.Writes.Count);
    }
}
