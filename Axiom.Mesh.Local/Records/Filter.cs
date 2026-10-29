using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>What a subscription receives of its topic (LOCAL-SPEC.md §5.18); a zero or empty field means any.</summary>
public sealed record Filter : IWireRecord<Filter>
{
    /// <summary>Only this Package; all zero for any.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>Only this device; all zero for any.</summary>
    public DeviceId DeviceId { get; init; }

    /// <summary>Only this user; all zero for any.</summary>
    public UserId UserId { get; init; }

    /// <summary>Only these kinds, Package or Entry kinds as the topic says, ascending; empty for any.</summary>
    public IReadOnlyList<ushort> Kinds { get; init; } = [];

    /// <summary>Only this operation; 0 for any.</summary>
    public uint OperationId { get; init; }

    /// <summary>Only this service; empty for any.</summary>
    public string Service { get; init; } = "";

    /// <summary>Only this telemetry series; empty for any.</summary>
    public string Series { get; init; } = "";

    /// <summary>Only this telemetry key; empty for any.</summary>
    public string Key { get; init; } = "";

    /// <summary>0 for live events only; else replay stored Entries from this store sequence first (§7.2).</summary>
    public ulong FromSeq { get; init; }

    /// <summary>The filter that reads nothing: every event of the topic, live only.</summary>
    public static Filter Any { get; } = new();

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Filter? value)
    {
        var read = new Filter
        {
            PackageId = PackageId.Read(ref reader),
            DeviceId = DeviceId.Read(ref reader),
            UserId = UserId.Read(ref reader),
            Kinds = LocalWire.ReadUInt16s(ref reader, FieldBounds.KindsCount),
            OperationId = reader.UInt32BigEndian(),
            Service = Wire.ReadString(ref reader, FieldBounds.FilterTextLength),
            Series = Wire.ReadString(ref reader, FieldBounds.FilterTextLength),
            Key = Wire.ReadString(ref reader, FieldBounds.TelemetryKeyLength),
            FromSeq = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        PackageId.Write(ref writer);
        DeviceId.Write(ref writer);
        UserId.Write(ref writer);
        LocalWire.WriteUInt16s(ref writer, Kinds, FieldBounds.KindsCount);
        writer.UInt32BigEndian(OperationId);
        Wire.WriteString(ref writer, Service, FieldBounds.FilterTextLength);
        Wire.WriteString(ref writer, Series, FieldBounds.FilterTextLength);
        Wire.WriteString(ref writer, Key, FieldBounds.TelemetryKeyLength);
        writer.UInt64BigEndian(FromSeq);
    }
}
