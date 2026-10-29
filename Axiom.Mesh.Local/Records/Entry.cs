using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A stored Entry (LOCAL-SPEC.md §5.6).</summary>
public sealed record Entry : IWireRecord<Entry>
{
    /// <summary>The Entry object.</summary>
    public ObjectHash Hash { get; init; }

    /// <summary>The store sequence: 1 for the first Entry this Host ever stored, then one more each time.</summary>
    public ulong StoreSeq { get; init; }

    /// <summary>When this Host stored it.</summary>
    public ulong Stored { get; init; }

    /// <summary>Its Package.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>The writing device.</summary>
    public DeviceId DeviceId { get; init; }

    /// <summary>Its Lamport clock.</summary>
    public ulong Clock { get; init; }

    /// <summary>Its parents, ascending as byte strings, as in the object.</summary>
    public IReadOnlyList<ObjectHash> Parents { get; init; } = [];

    /// <summary>Its kind (SPEC.md §14.7); unknown kinds are carried.</summary>
    public EntryKind Kind { get; init; }

    /// <summary>The payload bytes, as in the object.</summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>Whether an honoured Redaction names it; a user client MUST NOT display the payload then.</summary>
    public bool Redacted { get; init; }

    /// <summary>The Entry's position in the total order of SPEC.md §9.3.</summary>
    public EntryKey Key => new(Clock, DeviceId, Hash);

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Entry? value)
    {
        var read = new Entry
        {
            Hash = ObjectHash.Read(ref reader),
            StoreSeq = reader.UInt64BigEndian(),
            Stored = reader.UInt64BigEndian(),
            PackageId = PackageId.Read(ref reader),
            DeviceId = DeviceId.Read(ref reader),
            Clock = reader.UInt64BigEndian(),
            Parents = LocalWire.ReadHashes(ref reader, FieldBounds.ParentsCount),
            Kind = (EntryKind)reader.UInt16BigEndian(),
            Payload = LocalWire.ReadBlob(ref reader, FieldBounds.PayloadLength),
            Redacted = reader.Boolean(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        Hash.Write(ref writer);
        writer.UInt64BigEndian(StoreSeq);
        writer.UInt64BigEndian(Stored);
        PackageId.Write(ref writer);
        DeviceId.Write(ref writer);
        writer.UInt64BigEndian(Clock);
        LocalWire.WriteHashes(ref writer, Parents, FieldBounds.ParentsCount);
        writer.UInt16BigEndian((ushort)Kind);
        LocalWire.WriteBlob(ref writer, Payload, FieldBounds.PayloadLength);
        writer.Boolean(Redacted);
    }
}
