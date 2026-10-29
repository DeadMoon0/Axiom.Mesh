using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ServiceEvent</c> (1031, LOCAL-SPEC.md §7.3): A service called <c>Emit</c>.</summary>
public sealed record ServiceEvent : LocalEvent, IEvent<ServiceEvent>
{
    /// <summary>The message id, 1031 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 1031;

    /// <summary>The topic whose subscriptions receive it (LOCAL-SPEC.md §7.2).</summary>
    public static Topic Topic => Topic.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The service.</summary>
    public string Service { get; init; } = "";

    /// <summary>The service's event name.</summary>
    public string Event { get; init; } = "";

    /// <summary>The Package it concerns; all zero when none.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>The payload, the service's own.</summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ServiceEvent? value)
    {
        var read = new ServiceEvent
        {
            Service = Wire.ReadString(ref reader, FieldBounds.NameLength),
            Event = Wire.ReadString(ref reader, FieldBounds.NameLength),
            PackageId = PackageId.Read(ref reader),
            Payload = LocalWire.ReadBlob(ref reader, FieldBounds.PayloadLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Service, FieldBounds.NameLength);
        Wire.WriteString(ref writer, Event, FieldBounds.NameLength);
        PackageId.Write(ref writer);
        LocalWire.WriteBlob(ref writer, Payload, FieldBounds.PayloadLength);
    }
}
