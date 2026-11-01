using static Axiom.Mesh.Sync.Tests.Support.Facts;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// SYNC-SERVICE.md §3 step 5: the directory's state is its files by fingerprint and its directories, and every path the binding
/// leaves alone keeps the base's state, so leaving a path alone never reads as deleting it.
/// </summary>
public sealed class LocalTreeTests
{
    [Fact]
    public void FilesAndDirectoriesAsScanned_PathsLeftAloneAsTheBaseHadThem()
    {
        FlatTree baseTree = Folder(("a.txt", "a"), ("cache.tmp", "c"), ("build/out.bin", "o"), ("gone.txt", "g"));
        var print = new Fingerprint("a.txt", TreeEntryMode.File, 2, 1, File("a2").Hash, long.MaxValue);

        FlatTree local = LocalTree.Of([print], ["empty", "build"], baseTree, path => path.EndsWith(".tmp", System.StringComparison.Ordinal) || path.StartsWith("build/", System.StringComparison.Ordinal));

        Assert.Equal(["a.txt", "build", "build/out.bin", "cache.tmp", "empty"], local.Entries.Keys);
        Assert.Equal(File("a2").Hash, local.At("a.txt")!.Value.Hash);
        Assert.Equal(File("c").Hash, local.At("cache.tmp")!.Value.Hash);
    }
}
