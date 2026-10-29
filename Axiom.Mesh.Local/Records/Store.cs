using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>The object store and its budget (LOCAL-SPEC.md §5.10).</summary>
public sealed record Store : IWireRecord<Store>
{
    /// <summary>The store directory.</summary>
    public string Path { get; init; } = "";

    /// <summary>Stored objects.</summary>
    public ulong Objects { get; init; }

    /// <summary>Bytes of stored objects.</summary>
    public ulong Bytes { get; init; }

    /// <summary>The budget in bytes; 0 for the device default.</summary>
    public ulong Budget { get; init; }

    /// <summary>Bytes of objects with at least one pin.</summary>
    public ulong PinnedBytes { get; init; }

    /// <summary>Bytes of objects with no pin.</summary>
    public ulong EvictableBytes { get; init; }

    /// <summary>Bytes in the held buffer (SPEC.md §10.6).</summary>
    public ulong HeldBytes { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Store? value)
    {
        var read = new Store
        {
            Path = Wire.ReadString(ref reader, FieldBounds.PathLength),
            Objects = reader.UInt64BigEndian(),
            Bytes = reader.UInt64BigEndian(),
            Budget = reader.UInt64BigEndian(),
            PinnedBytes = reader.UInt64BigEndian(),
            EvictableBytes = reader.UInt64BigEndian(),
            HeldBytes = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Path, FieldBounds.PathLength);
        writer.UInt64BigEndian(Objects);
        writer.UInt64BigEndian(Bytes);
        writer.UInt64BigEndian(Budget);
        writer.UInt64BigEndian(PinnedBytes);
        writer.UInt64BigEndian(EvictableBytes);
        writer.UInt64BigEndian(HeldBytes);
    }
}
