using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>The result body of a Done <c>PutFile</c> operation (LOCAL-SPEC.md §8.6): the ChunkList.</summary>
public sealed record PutFileResult : IWireRecord<PutFileResult>
{
    /// <summary>The ChunkList.</summary>
    public ObjectHash Hash { get; init; }

    /// <summary>The file's size.</summary>
    public ulong Size { get; init; }

    /// <summary>Its Chunks.</summary>
    public uint Chunks { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out PutFileResult? value)
    {
        var read = new PutFileResult
        {
            Hash = ObjectHash.Read(ref reader),
            Size = reader.UInt64BigEndian(),
            Chunks = reader.UInt32BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Hash.Write(ref writer);
        writer.UInt64BigEndian(Size);
        writer.UInt32BigEndian(Chunks);
    }
}
