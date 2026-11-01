using System.Collections.Generic;
using System.Linq;
using static Axiom.Mesh.Sync.Tests.Support.Facts;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// SPEC.md §9.5, the merge table, path by path over the union of two leaves' Trees against the common ancestor's: one test per
/// line of the table, then a directory replaced by a file, no content merge, symmetry and the empty ancestor. Ours is the
/// laptop's leaf (clock 5), theirs the desktop's (clock 6): theirs is later in the total order.
/// </summary>
public sealed class MergeTableTests
{
    private static readonly EntryKey Ours = Key(5, Laptop, 0x51);
    private static readonly EntryKey Theirs = Key(6, Desktop, 0x52);
    private static readonly string LaptopTag = ".conflict-" + Laptop.Value.ToHex()[..8];
    private static readonly string DesktopTag = ".conflict-" + Desktop.Value.ToHex()[..8];

    [Fact]
    public void ChangedOnNeither_TheAncestorsEntry()
    {
        var trees = new Dictionary<ObjectHash, Tree>();
        Tree ancestor = Tree(trees, ("a.txt", "a"), ("b.txt", "b"));
        Tree ours = Tree(trees, ("a.txt", "a"), ("b.txt", "ours"));
        Tree theirs = Tree(trees, ("a.txt", "a"), ("b.txt", "b"));

        MergedTree merged = MergeOf(ancestor, ours, theirs, trees);

        Assert.Equal(File("a").Hash, FilesOf(merged.Root, trees)["a.txt"]);
    }

    [Fact]
    public void ChangedOnOneSideOnly_ThatSidesState_ADeletionIncluded()
    {
        var trees = new Dictionary<ObjectHash, Tree>();
        Tree ancestor = Tree(trees, ("kept.txt", "k"), ("edited.txt", "old"), ("deleted.txt", "d"));
        Tree ours = Tree(trees, ("kept.txt", "k"), ("edited.txt", "new"), ("deleted.txt", "d"), ("added.txt", "+"));
        Tree theirs = Tree(trees, ("kept.txt", "k"), ("edited.txt", "old"));

        IReadOnlyDictionary<string, ObjectHash> files = FilesOf(MergeOf(ancestor, ours, theirs, trees).Root, trees);

        Assert.Equal(["added.txt", "edited.txt", "kept.txt"], files.Keys.Order());
        Assert.Equal(File("new").Hash, files["edited.txt"]);
    }

    [Fact]
    public void ChangedOnBothIdentically_ThatEntry_NoCopy()
    {
        var trees = new Dictionary<ObjectHash, Tree>();
        Tree ancestor = Tree(trees, ("a.txt", "old"));
        Tree ours = Tree(trees, ("a.txt", "same"));
        Tree theirs = Tree(trees, ("a.txt", "same"));

        MergedTree merged = MergeOf(ancestor, ours, theirs, trees);

        Assert.Equal(File("same").Hash, FilesOf(merged.Root, trees)["a.txt"]);
        Assert.Empty(merged.Copies);
        Assert.True(merged.EqualsLater);
    }

    // The later leaf's entry stands; the earlier side's file lies beside it under <name>.conflict-<8 hex of its device>, before
    // the extension, and a deletion on the later side still keeps the earlier side's edited file as the copy.
    [Fact]
    public void ChangedOnBothDifferently_TheLaterLeafsEntry_TheOtherSidesFileBeside()
    {
        var trees = new Dictionary<ObjectHash, Tree>();
        Tree ancestor = Tree(trees, ("report.txt", "v1"), ("gone.md", "g"));
        Tree ours = Tree(trees, ("report.txt", "laptop's"), ("gone.md", "edited on the laptop"));
        Tree theirs = Tree(trees, ("report.txt", "desktop's"));

        MergedTree merged = MergeOf(ancestor, ours, theirs, trees);
        IReadOnlyDictionary<string, ObjectHash> files = FilesOf(merged.Root, trees);

        Assert.Equal(File("desktop's").Hash, files["report.txt"]);
        Assert.Equal(File("laptop's").Hash, files["report" + LaptopTag + ".txt"]);
        Assert.False(files.ContainsKey("gone.md"));
        Assert.Equal(File("edited on the laptop").Hash, files["gone" + LaptopTag + ".md"]);
        Assert.Equal([("gone.md", "gone" + LaptopTag + ".md"), ("report.txt", "report" + LaptopTag + ".txt")], merged.Copies.Select(c => (c.Path, c.CopyPath)));
        Assert.All(merged.Copies, c => Assert.Equal(Laptop, c.OtherDevice));
    }

    // A directory replaced by a file, or the reverse, is changed differently: the later side stands and the other side's file
    // or whole subtree lies beside it.
    [Fact]
    public void ADirectoryReplacedByAFileIsChangedDifferently_TheOtherSidesSubtreeLiesBeside()
    {
        var trees = new Dictionary<ObjectHash, Tree>();
        Tree ancestor = Tree(trees, ("docs/a.txt", "a"));
        Tree oursDirectory = Tree(trees, ("docs/a.txt", "a"), ("docs/b.txt", "b"));
        Tree theirsFile = Tree(trees, ("docs", "now a file"));

        IReadOnlyDictionary<string, ObjectHash> fileWins = FilesOf(MergeOf(ancestor, oursDirectory, theirsFile, trees).Root, trees);
        IReadOnlyDictionary<string, ObjectHash> directoryWins = FilesOf(Merge(ancestor, theirsFile, Key(9, Desktop, 1), oursDirectory, Key(10, Laptop, 2), trees).Root, trees);

        Assert.Equal(["docs", "docs" + LaptopTag + "/a.txt", "docs" + LaptopTag + "/b.txt"], fileWins.Keys.Order());
        Assert.Equal(["docs" + DesktopTag, "docs/a.txt", "docs/b.txt"], directoryWins.Keys.Order());
    }

    [Fact]
    public void NoContentIsMerged_TwoEditsOfOneTextKeepBothWhole()
    {
        var trees = new Dictionary<ObjectHash, Tree>();
        Tree ancestor = Tree(trees, ("notes.txt", "line 1\nline 2\n"));
        Tree ours = Tree(trees, ("notes.txt", "line 1 edited\nline 2\n"));
        Tree theirs = Tree(trees, ("notes.txt", "line 1\nline 2 edited\n"));

        IReadOnlyDictionary<string, ObjectHash> files = FilesOf(MergeOf(ancestor, ours, theirs, trees).Root, trees);

        Assert.Equal(File("line 1\nline 2 edited\n").Hash, files["notes.txt"]);
        Assert.Equal(File("line 1 edited\nline 2\n").Hash, files["notes" + LaptopTag + ".txt"]);
    }

    [Fact]
    public void TheMergeIsTheSameWhicheverLeafIsCalledOurs()
    {
        var trees = new Dictionary<ObjectHash, Tree>();
        Tree ancestor = Tree(trees, ("a/x.txt", "x"), ("b.txt", "b"), ("c.txt", "c"));
        Tree ours = Tree(trees, ("a/x.txt", "x laptop"), ("b.txt", "b laptop"), ("d/new.txt", "n"));
        Tree theirs = Tree(trees, ("a/x.txt", "x desktop"), ("c.txt", "c desktop"), ("d/new.txt", "other"));

        MergedTree one = Merge(ancestor, ours, Ours, theirs, Theirs, trees);
        MergedTree other = Merge(ancestor, theirs, Theirs, ours, Ours, trees);

        Assert.Equal(one.Root, other.Root);
        Assert.Equal(one.Copies, other.Copies);
    }

    // No common ancestor: both sides are additions against the empty tree, the same path with other bytes conflicts.
    [Fact]
    public void WithoutACommonAncestorBothSidesAreAdditions_TheSamePathWithOtherBytesConflicts()
    {
        var trees = new Dictionary<ObjectHash, Tree>();
        Tree ours = Tree(trees, ("same.txt", "s"), ("mine.txt", "m"), ("clash.txt", "laptop"));
        Tree theirs = Tree(trees, ("same.txt", "s"), ("yours.txt", "y"), ("clash.txt", "desktop"));

        IReadOnlyDictionary<string, ObjectHash> files = FilesOf(MergeOf(TreeSet.EmptyTree, ours, theirs, trees).Root, trees);

        Assert.Equal(["clash" + LaptopTag + ".txt", "clash.txt", "mine.txt", "same.txt", "yours.txt"], files.Keys.Order());
    }

    private static MergedTree MergeOf(Tree ancestor, Tree ours, Tree theirs, Dictionary<ObjectHash, Tree> trees) => Merge(ancestor, ours, Ours, theirs, Theirs, trees);

    private static MergedTree Merge(Tree ancestor, Tree ours, EntryKey ourKey, Tree theirs, EntryKey theirKey, Dictionary<ObjectHash, Tree> trees)
    {
        Assert.True(Sync.Merge.TryOf(ancestor, ours, theirs, ourKey, theirKey, trees, out MergedTree? merged, out string? problem), problem);
        foreach (Tree made in merged!.Made)
            trees[made.Hash] = made;
        return merged;
    }
}
