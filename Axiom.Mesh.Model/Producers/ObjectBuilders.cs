using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>Builds the unsigned objects (SPEC.md §8.2–§8.4), each parsed back before it is returned.</summary>
public static class ObjectBuilders
{
    /// <summary>A Chunk holding <paramref name="data"/> (SPEC.md §8.2).</summary>
    /// <param name="data">1 byte to 1 MiB.</param>
    /// <returns>The Chunk.</returns>
    /// <exception cref="ArgumentException">The data is empty or longer than 1 MiB.</exception>
    public static Chunk Chunk(ReadOnlySpan<byte> data)
    {
        if (data.Length is < Limits.MinChunkLength or > Limits.ChunkLength)
            throw new ArgumentException($"A Chunk holds 1 to {Limits.ChunkLength} bytes; {data.Length} were given (SPEC.md §8.2).", nameof(data));

        // One array of the exact size, the data copied once behind the header: the Chunk owns its bytes.
        byte[] bytes = new byte[Chunker.HeaderLength + data.Length];
        bytes[0] = (byte)ObjectTag.Chunk;
        bytes[1] = Objects.Version;
        data.CopyTo(bytes.AsSpan(Chunker.HeaderLength));
        return ObjectEncoding.ParseOwn<Chunk>(bytes);
    }

    /// <summary>A ChunkList (SPEC.md §8.3).</summary>
    /// <param name="size">The file's size; the Chunks' lengths must add up to it.</param>
    /// <param name="chunks">The Chunks' hashes in file order; at most 32 768.</param>
    /// <returns>The ChunkList.</returns>
    /// <exception cref="ArgumentException">More than 32 768 Chunks.</exception>
    public static ChunkList ChunkList(ulong size, IReadOnlyList<ObjectHash> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArrayBufferWriter<byte> output = ObjectEncoding.Start(ObjectTag.ChunkList);
        var writer = new WireWriter(output);
        writer.UInt64BigEndian(size);
        Wire.WriteBigListCount(ref writer, chunks.Count, Limits.MaxChunkListChunks);
        foreach (ObjectHash chunk in chunks)
            chunk.Write(ref writer);
        return ObjectEncoding.ParseOwn<ChunkList>(ObjectEncoding.Finish(output));
    }

    /// <summary>
    /// A Tree of <paramref name="entries"/> in any order (SPEC.md §8.4); they are sorted by name as byte strings. Fails
    /// for what a directory on disk can hold but a Tree may not: a repeated or unsafe name, a bad mode or size.
    /// </summary>
    /// <param name="entries">The entries; at most 8 192.</param>
    /// <param name="tree">The Tree, or <see langword="null"/>.</param>
    /// <param name="problem">Why the entries make no Tree, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the Tree was built.</returns>
    public static bool TryTree(IEnumerable<TreeEntry> entries, [NotNullWhen(true)] out Tree? tree, [NotNullWhen(false)] out string? problem)
    {
        ArgumentNullException.ThrowIfNull(entries);
        tree = null;
        (byte[] Name, TreeEntry Entry)[] sorted = [.. entries.Select(e => (Encoding.UTF8.GetBytes(e.Name ?? string.Empty), e)).OrderBy(e => e.Item1, ByteStrings.Comparer)];
        problem = sorted.Length > Limits.MaxTreeEntries ? $"A Tree holds at most {Limits.MaxTreeEntries} entries; {sorted.Length} were given (SPEC.md §8.4)." : null;
        for (int i = 0; i < sorted.Length && problem is null; i++)
        {
            problem = (sorted[i].Name.Length > Limits.MaxTreeNameLength ? $"The name {sorted[i].Entry.Name} is longer than {Limits.MaxTreeNameLength} bytes (SPEC.md §8.4)." : null)
                ?? Tree.NameProblem(sorted[i].Name)
                ?? Tree.ModeProblem(sorted[i].Entry.Mode, sorted[i].Entry.Size)
                ?? (i > 0 && sorted[i - 1].Name.AsSpan().SequenceEqual(sorted[i].Name) ? $"The name {sorted[i].Entry.Name} repeats (SPEC.md §8.4)." : null)
                ?? (!Wire.IsWellFormedUtf16(sorted[i].Entry.Name ?? string.Empty) ? $"The name {sorted[i].Entry.Name} is not valid UTF-16 (SPEC.md §5.2)." : null);
        }

        if (problem is not null)
            return false;

        ArrayBufferWriter<byte> output = ObjectEncoding.Start(ObjectTag.Tree);
        var writer = new WireWriter(output);
        Wire.WriteListCount(ref writer, sorted.Length, Limits.MaxTreeEntries);
        foreach ((_, TreeEntry entry) in sorted)
        {
            Wire.WriteString(ref writer, entry.Name, Limits.MaxTreeNameLength);
            writer.UInt8((byte)entry.Mode);
            entry.Hash.Write(ref writer);
            writer.UInt64BigEndian(entry.Size);
        }

        tree = ObjectEncoding.ParseOwn<Tree>(ObjectEncoding.Finish(output));
        return true;
    }
}
