using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>
/// SPEC.md §8.4 and §11.4 as the client's helpers apply them before a byte is written: which names a file system holds,
/// which paths a Tree becomes, and what an existing directory allows (LOCAL-SPEC.md draft 4, §9.5).
/// </summary>
public sealed class MaterialiseRuleTests
{
    // §8.4, §11.4: a name is written only when the file system can hold it as one file name inside the directory;
    // anything else is a conflict, never a write somewhere else.
    [Theory]
    [InlineData("report.txt", false, true)] // an ordinary name, on Linux
    [InlineData("report.txt", true, true)] // and on Windows
    [InlineData("a:b", false, true)] // a colon is an ordinary character on Linux
    [InlineData("a:b", true, false)] // on Windows it would name an alternate data stream
    [InlineData("trailing.", true, false)] // Windows drops a trailing dot, so the name would not be the Tree's
    [InlineData("trailing ", true, false)] // or a trailing space
    [InlineData("con.txt", true, false)] // a reserved device name, whatever its extension
    [InlineData("console.txt", true, true)] // a name that only starts like one is fine
    [InlineData("back\\slash", true, false)] // a separator on Windows
    [InlineData("tab\there", true, false)] // a control character Windows refuses in a name
    [InlineData("..", false, false)] // never the parent
    [InlineData(".axiom-mesh-materialised", false, false)] // the helpers' own marker
    [InlineData(".axiom-mesh-partial-x", false, false)] // the helpers' own temporary name
    public void ANameIsWrittenOnlyWhenTheFileSystemHoldsItAsOneNameInTheDirectory(string name, bool windows, bool written)
    {
        Assert.Equal(written, FileNameRule.MayWrite(name, windows));
    }

    // §8.4, §11.4: a Tree becomes its directories (parents first) and files with paths under the target; a name the file
    // system cannot hold, a case-insensitive collision and a directory whose Tree is not at hand are conflicts.
    [Fact]
    public void ATreeBecomesItsDirectoriesAndFiles_WhatTheFileSystemCannotHoldIsAConflict()
    {
        ChunkList file = ObjectBuilders.ChunkList(3, [ObjectBuilders.Chunk("abc"u8).Hash]);
        Chunk link = ObjectBuilders.Chunk("target"u8);
        Tree inner = Tree(new TreeEntry("c.txt", TreeEntryMode.ExecutableFile, file.Hash, 3));
        Tree missing = Tree(new TreeEntry("never.txt", TreeEntryMode.File, file.Hash, 3));
        Tree docs = Tree(new TreeEntry("inner", TreeEntryMode.Directory, inner.Hash, 0), new TreeEntry("b.txt", TreeEntryMode.File, file.Hash, 3));
        Tree root = Tree(
            new TreeEntry("a.txt", TreeEntryMode.File, file.Hash, 3),
            new TreeEntry("A.TXT", TreeEntryMode.File, file.Hash, 3),
            new TreeEntry("docs", TreeEntryMode.Directory, docs.Hash, 0),
            new TreeEntry("gone", TreeEntryMode.Directory, missing.Hash, 0),
            new TreeEntry("link", TreeEntryMode.Symlink, link.Hash, 6),
            new TreeEntry("x:y", TreeEntryMode.File, file.Hash, 3));
        var trees = new Dictionary<ObjectHash, Tree> { [docs.Hash] = docs, [inner.Hash] = inner };

        MaterialisePlan windows = MaterialisePlan.Of(root, trees, windows: true);
        MaterialisePlan linux = MaterialisePlan.Of(root, trees, windows: false);

        Assert.Equal(["docs", "docs/inner"], windows.Directories);
        Assert.Equal(
            [("A.TXT", PlannedFileKind.File), ("docs/b.txt", PlannedFileKind.File), ("docs/inner/c.txt", PlannedFileKind.Executable), ("link", PlannedFileKind.Link)],
            windows.Files.Select(f => (f.Path, f.Kind)));
        Assert.Equal(["a.txt", "gone", "x:y"], windows.Conflicts.Order());
        Assert.Contains("x:y", linux.Files.Select(f => f.Path));
        Assert.Equal(3ul + 3 + 3 + 6, windows.TotalBytes);
    }

    // LOCAL-SPEC.md draft 4 (the helper's replace mode, formerly §6.4 existing): Fail refuses anything there; Replace
    // deletes a regular file or a directory materialised earlier (it holds the marker), never another directory; Merge
    // writes into a directory; nothing is written through a link; an unknown mode is Invalid.
    [Theory]
    [InlineData(ExistingPath.Fail, "Absent", "Create", Status.Ok)] // nothing there: create
    [InlineData(ExistingPath.Fail, "MarkedDirectory", "Refuse", Status.AlreadyExists)] // Fail: anything there
    [InlineData(ExistingPath.Replace, "MarkedDirectory", "ReplaceDirectory", Status.Ok)] // a directory materialised earlier
    [InlineData(ExistingPath.Replace, "Directory", "Refuse", Status.Invalid)] // someone else's directory is never deleted
    [InlineData(ExistingPath.Replace, "File", "ReplaceFile", Status.Ok)] // a regular file may go
    [InlineData(ExistingPath.Replace, "Link", "Refuse", Status.Invalid)] // never through a link
    [InlineData(ExistingPath.Merge, "Directory", "WriteInto", Status.Ok)] // into any directory
    [InlineData(ExistingPath.Merge, "File", "Refuse", Status.Invalid)] // not into a file
    [InlineData(ExistingPath.Unassigned, "Absent", "Refuse", Status.Invalid)] // an unknown mode
    public void WhatIsAtTheTargetDecidesWhetherItIsCreatedReplacedWrittenIntoOrRefused(ExistingPath mode, string state, string action, Status status)
    {
        ExistingDecision decision = ExistingRule.Decide(mode, System.Enum.Parse<PathState>(state));
        Assert.Equal((System.Enum.Parse<ExistingAction>(action), status), (decision.Action, decision.Status));
    }

    // LOCAL-SPEC.md §9.7 and decision 17: direct reads are for a service the Host reports the store readable to; the file of
    // an object is objects/<h0h1>/<h2h3>/<hash>.
    [Fact]
    public void OnlyAServiceToWhichTheStoreIsReadableReadsItDirectly_InLayout1()
    {
        Assert.True(ObjectReader.MayReadDirectly(Ack(Role.Service, HostCapabilities.StoreReadable)));
        Assert.False(ObjectReader.MayReadDirectly(Ack(Role.Service, HostCapabilities.None)));
        Assert.False(ObjectReader.MayReadDirectly(Ack(Role.User, HostCapabilities.StoreReadable)));

        ObjectHash hash = ObjectBuilders.Chunk("x"u8).Hash;
        string hex = hash.Value.ToHex();
        Assert.Equal(System.IO.Path.Combine("store", "objects", hex[..2], hex[2..4], hex), ObjectReader.FileOf("store", hash));
    }

    private static HelloAck Ack(Role role, HostCapabilities capabilities) => new() { Role = role, Capabilities = capabilities, Identity = new Identity() };

    private static Tree Tree(params TreeEntry[] entries)
    {
        Assert.True(ObjectBuilders.TryTree(entries, out Tree? tree, out string? problem), problem);
        return tree;
    }
}
