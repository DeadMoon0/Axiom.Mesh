using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>The result body of a Done <c>Fetch</c> operation (LOCAL-SPEC.md §8.6).</summary>
public sealed record FetchResult : IWireRecord<FetchResult>
{
    /// <summary>Objects fetched or found stored.</summary>
    public uint Objects { get; init; }

    /// <summary>Their bytes.</summary>
    public ulong Bytes { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out FetchResult? value)
    {
        var read = new FetchResult
        {
            Objects = reader.UInt32BigEndian(),
            Bytes = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        writer.UInt32BigEndian(Objects);
        writer.UInt64BigEndian(Bytes);
    }
}
