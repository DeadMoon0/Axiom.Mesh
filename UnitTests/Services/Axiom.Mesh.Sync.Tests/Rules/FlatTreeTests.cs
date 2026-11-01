using System;
using System.Collections.Generic;
using System.Linq;
using static Axiom.Mesh.Sync.Tests.Support.Facts;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// SPEC.md §8.4: a folder as paths and as Trees are the same thing: files, executable files, symlinks and empty directories
/// survive flattening and building again, down to the root hash.
/// </summary>
public sealed class FlatTreeTests
{
    [Fact]
    public void FlatteningATreeAndBuildingItAgainGivesTheSameRootHash()
    {
        Chunk link = ObjectBuilders.Chunk("../target"u8);
        FlatTree folder = FlatTree.Of(
        [
            new("a.txt", File("alpha")),
            new("bin/run", Executable("#!/bin/sh")),
            new("docs/deep/x.md", File("x")),
            new("empty", FlatEntry.Directory),
            new("link", new FlatEntry(TreeEntryMode.Symlink, link.Hash, (ulong)link.Data.Length)),
        ]);

        (Tree root, Dictionary<ObjectHash, Tree> trees) = TreesOf(folder);
        Assert.True(TreeFlattener.TryOf(root, trees, out FlatTree again, out _));
        (Tree rebuilt, _) = TreesOf(again);

        Assert.Equal(root.Hash, rebuilt.Hash);
        Assert.Equal(folder.Entries.ToArray(), again.Entries.ToArray());
        Assert.True(again.At("empty")!.Value.IsDirectory);
        Assert.Equal(TreeEntryMode.ExecutableFile, again.At("bin/run")!.Value.Mode);
    }

    [Fact]
    public void EveryDirectoryAPathLiesInIsAPathOfItsOwn_APathBelowAFileIsRefused()
    {
        FlatTree folder = Folder(("a/b/c.txt", "c"));

        Assert.Equal(["a", "a/b", "a/b/c.txt"], folder.Entries.Keys);
        Assert.Throws<ArgumentException>(() => FlatTree.Of([new("a", File("file")), new("a/b", File("below a file"))]));
        Assert.False(TreeFlattener.TryOf(TreesOf(folder).Root, new Dictionary<ObjectHash, Tree>(), out _, out ObjectHash missing));
        Assert.NotEqual(default, missing);
    }
}
