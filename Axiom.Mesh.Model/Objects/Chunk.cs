using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>File contents: 1 byte to 1 MiB of data (SPEC.md §8.2). Storable as parsed; it needs nothing but its hash.</summary>
public sealed class Chunk : StorableObject
{
    private Chunk(HashedObject source, ReadOnlyMemory<byte> data)
        : base(source.Hash, source.Bytes) => Data = data;

    /// <summary>The data, the rest of the object after its tag and version.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <inheritdoc />
    public override ObjectTag Tag => ObjectTag.Chunk;

    internal static Chunk? Parse(HashedObject source, ref WireReader reader, out ObjectFault fault)
    {
        ReadOnlyMemory<byte> data = reader.RestView();
        if (data.Length is < Limits.MinChunkLength or > Limits.ChunkLength)
        {
            ObjectReading.Fail($"A Chunk holds 1 to {Limits.ChunkLength} bytes; this one holds {data.Length} (SPEC.md §8.2).", out fault);
            return null;
        }

        return ObjectReading.AtEnd(ref reader, "Chunk", out fault) ? new Chunk(source, data) : null;
    }
}
