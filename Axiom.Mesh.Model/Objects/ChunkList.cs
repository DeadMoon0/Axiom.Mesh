using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// A file: its size and its Chunks in order (SPEC.md §8.3). Storable as parsed; that <see cref="Size"/> equals the sum
/// of the Chunks' lengths can only be checked once they are held (<see cref="ChunkListRule"/>).
/// </summary>
public sealed class ChunkList : StorableObject
{
    private ChunkList(HashedObject source, ulong size, HashList chunks)
        : base(source.Hash, source.Bytes)
    {
        Size = size;
        Chunks = chunks;
    }

    /// <summary>The file's size in bytes, authoritative for its length.</summary>
    public ulong Size { get; }

    /// <summary>The Chunks' hashes, in file order; at most 32 768.</summary>
    public HashList Chunks { get; }

    /// <inheritdoc />
    public override ObjectTag Tag => ObjectTag.ChunkList;

    internal static ChunkList? Parse(HashedObject source, ref WireReader reader, out ObjectFault fault)
    {
        ulong size = reader.UInt64BigEndian();
        int count = Wire.ReadBigListCount(ref reader, Limits.MaxChunkListChunks);
        HashList chunks = new(reader.BytesView(count * Hash32.Length));
        return ObjectReading.AtEnd(ref reader, "ChunkList", out fault) ? new ChunkList(source, size, chunks) : null;
    }
}
