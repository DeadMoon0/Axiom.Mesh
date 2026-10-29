using System;
using System.Collections.Generic;
using System.Text;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// A directory listing (SPEC.md §8.4): entries sorted by name as byte strings, names unique and safe. Storable as
/// parsed; the objects it names are authoritative over its sizes.
/// </summary>
public sealed class Tree : StorableObject
{
    private Tree(HashedObject source, TreeEntry[] entries)
        : base(source.Hash, source.Bytes) => Entries = entries;

    /// <summary>The entries, in ascending order of their names' UTF-8 bytes.</summary>
    public IReadOnlyList<TreeEntry> Entries { get; }

    /// <inheritdoc />
    public override ObjectTag Tag => ObjectTag.Tree;

    /// <summary>Why a name may not stand in a Tree (SPEC.md §8.4), or <see langword="null"/> when it may.</summary>
    /// <param name="utf8">The name's UTF-8 bytes, already a valid <c>string(255)</c>.</param>
    /// <returns>The rule broken, or <see langword="null"/>.</returns>
    internal static string? NameProblem(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
            return "A Tree entry's name is empty (SPEC.md §8.4).";
        if (utf8.SequenceEqual("."u8) || utf8.SequenceEqual(".."u8))
            return "A Tree entry is named . or .. (SPEC.md §8.4).";
        if (utf8.IndexOfAny((byte)'/', (byte)'\\', (byte)0) >= 0)
            return @"A Tree entry's name holds /, \ or NUL (SPEC.md §8.4).";
        return null;
    }

    /// <summary>Why a mode and size may not stand together (SPEC.md §8.4), or <see langword="null"/>.</summary>
    /// <param name="mode">The mode.</param>
    /// <param name="size">The size.</param>
    /// <returns>The rule broken, or <see langword="null"/>.</returns>
    internal static string? ModeProblem(TreeEntryMode mode, ulong size) => mode switch
    {
        TreeEntryMode.File or TreeEntryMode.ExecutableFile or TreeEntryMode.Symlink => null,
        TreeEntryMode.Directory => size == 0 ? null : "A directory entry's size is not 0 (SPEC.md §8.4).",
        _ => $"A Tree entry's mode is {(byte)mode}; only 1 to 4 are defined (SPEC.md §8.4).",
    };

    internal static Tree? Parse(HashedObject source, ref WireReader reader, out ObjectFault fault)
    {
        int count = Wire.ReadListCount(ref reader, Limits.MaxTreeEntries);
        var entries = new TreeEntry[count];
        ReadOnlySpan<byte> previous = default;
        for (int i = 0; i < count && reader.Ok; i++)
        {
            ReadOnlyMemory<byte> name = Wire.ReadStringBytes(ref reader, Limits.MaxTreeNameLength);
            var mode = (TreeEntryMode)reader.UInt8();
            ObjectHash hash = ObjectHash.Read(ref reader);
            ulong size = reader.UInt64BigEndian();
            if (!reader.Ok)
                break;

            string? problem = NameProblem(name.Span) ?? ModeProblem(mode, size);
            if (problem is null && i > 0 && previous.SequenceCompareTo(name.Span) >= 0)
                problem = "Tree entries are not sorted by name as byte strings, or a name repeats (SPEC.md §8.4).";
            if (problem is not null)
            {
                ObjectReading.Fail(problem, out fault);
                return null;
            }

            entries[i] = new TreeEntry(Encoding.UTF8.GetString(name.Span), mode, hash, size);
            previous = name.Span;
        }

        return ObjectReading.AtEnd(ref reader, "Tree", out fault) ? new Tree(source, entries) : null;
    }
}
