using System.Linq;
using System.Text;
using Axiom.Mesh.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §8.4: a Tree's entries, their order, names, modes and sizes.</summary>
public sealed class TreeTests
{
    // §8.4: "Entries MUST be sorted by name as byte strings and MUST be unique; otherwise the Tree is malformed." Byte
    // order is not culture order: "B" (0x42) precedes "a" (0x61), and "z" (0x7A) precedes "é" (0xC3 0xA9).
    [Fact]
    public void ATreeWhoseEntriesAreNotSortedByNameAsByteStringsIsMalformed()
    {
        Assert.Equal(2, Samples.Parse<Tree>(RawTree(("B", 1, 0), ("a", 1, 0))).Entries.Count);
        Assert.Equal(2, Samples.Parse<Tree>(RawTree(("z", 1, 0), ("é", 1, 0))).Entries.Count);

        Assert.Equal(ObjectFaultKind.Malformed, Samples.Fault(RawTree(("a", 1, 0), ("B", 1, 0))).Kind);
        Assert.Equal(ObjectFaultKind.Malformed, Samples.Fault(RawTree(("é", 1, 0), ("z", 1, 0))).Kind);
        Assert.Equal(ObjectFaultKind.Malformed, Samples.Fault(RawTree(("same", 1, 0), ("same", 1, 0))).Kind);

        // The builder sorts by the same order, whatever order the entries come in.
        Assert.True(ObjectBuilders.TryTree([Entry("é"), Entry("a"), Entry("z"), Entry("B")], out Tree? tree, out _));
        Assert.Equal(new[] { "B", "a", "z", "é" }, tree.Entries.Select(e => e.Name));
    }

    // §8.4: "A name MUST NOT be empty, . or .., and MUST NOT contain /, \ or NUL (malformed)"; "mode … other values:
    // malformed"; "size … 3: MUST be 0 (else malformed)".
    [Theory]
    // An empty name.
    [InlineData("", (byte)1, 0UL)]
    // The current directory.
    [InlineData(".", (byte)1, 0UL)]
    // The parent directory.
    [InlineData("..", (byte)3, 0UL)]
    // A slash, which would make a path.
    [InlineData("a/b", (byte)1, 0UL)]
    // A backslash, a path on Windows.
    [InlineData("a\\b", (byte)1, 0UL)]
    // A NUL, also refused by every string of §5.2.
    [InlineData("a\0b", (byte)1, 0UL)]
    // Mode 0 is defined by nothing.
    [InlineData("a", (byte)0, 0UL)]
    // Mode 5 is defined by nothing.
    [InlineData("a", (byte)5, 0UL)]
    // A directory with a size.
    [InlineData("a", (byte)3, 1UL)]
    public void AnUnsafeNameAnUndefinedModeOrASizedDirectoryIsMalformed(string name, byte mode, ulong size)
    {
        Assert.Equal(ObjectFaultKind.Malformed, Samples.Fault(RawTree((name, mode, size))).Kind);
        Assert.False(ObjectBuilders.TryTree([new TreeEntry(name, (TreeEntryMode)mode, RawObjects.Hash(1), size)], out _, out string? problem));
        Assert.NotNull(problem);
    }

    // §8.4: names like "..." or ".hidden" are ordinary names; only "." and ".." are refused.
    [Fact]
    public void DotsInsideANameAreOrdinary()
    {
        Assert.Equal(3, Samples.Parse<Tree>(RawTree(("...", 1, 0), (".hidden", 2, 7), ("link", 4, 3))).Entries.Count);
    }

    private static TreeEntry Entry(string name) => new(name, TreeEntryMode.File, RawObjects.Hash(1), 0);

    private static byte[] RawTree(params (string Name, byte Mode, ulong Size)[] entries) =>
        RawObjects.Unsigned((byte)ObjectTag.Tree, output =>
        {
            var writer = new WireWriter(output);
            writer.UInt16BigEndian((ushort)entries.Length);
            foreach ((string name, byte mode, ulong size) in entries)
            {
                RawObjects.String(ref writer, Encoding.UTF8.GetBytes(name));
                writer.UInt8(mode);
                RawObjects.Hash(1).Write(ref writer);
                writer.UInt64BigEndian(size);
            }
        });
}
