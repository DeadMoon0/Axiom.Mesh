using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A device of a known user (LOCAL-SPEC.md §5.3).</summary>
public sealed record Device : IWireRecord<Device>
{
    /// <summary>The device.</summary>
    public DeviceId DeviceId { get; init; }

    /// <summary>Its user.</summary>
    public UserId UserId { get; init; }

    /// <summary>Its name, from the Roster.</summary>
    public string Name { get; init; } = "";

    /// <summary>When it was added, from the Roster; informational.</summary>
    public ulong AddedAt { get; init; }

    /// <summary>Whether it is removed.</summary>
    public bool Removed { get; init; }

    /// <summary>Whether it is this device.</summary>
    public bool Self { get; init; }

    /// <summary>The end of the last established session with it; 0 when never.</summary>
    public ulong LastSeen { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Device? value)
    {
        var read = new Device
        {
            DeviceId = DeviceId.Read(ref reader),
            UserId = UserId.Read(ref reader),
            Name = Wire.ReadString(ref reader, FieldBounds.DeviceNameLength),
            AddedAt = reader.UInt64BigEndian(),
            Removed = reader.Boolean(),
            Self = reader.Boolean(),
            LastSeen = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        DeviceId.Write(ref writer);
        UserId.Write(ref writer);
        Wire.WriteString(ref writer, Name, FieldBounds.DeviceNameLength);
        writer.UInt64BigEndian(AddedAt);
        writer.Boolean(Removed);
        writer.Boolean(Self);
        writer.UInt64BigEndian(LastSeen);
    }
}
