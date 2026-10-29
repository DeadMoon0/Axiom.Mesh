using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>Emit</c> (518, LOCAL-SPEC.md §9.10): Publishes a <c>ServiceEvent</c> to every matching subscription of topic <c>service</c>.</summary>
public sealed record Emit : LocalRequest, IRequest<Emit, NoContent>
{
    /// <summary>The message id, 518 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 518;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "Emit";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §9.10).</summary>
    public static Roles AllowedRoles => Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The service's own event name.</summary>
    public required string Event { get; init; }

    /// <summary>The Package it concerns; all zero when none.</summary>
    public PackageId PackageId { get; init; }

    /// <summary>The event's payload.</summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out NoContent ok)
    {
        ok = default;
        return true;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, NoContent ok)
    {
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Emit? value)
    {
        var read = new Emit
        {
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
        Wire.WriteString(ref writer, Event, FieldBounds.NameLength);
        PackageId.Write(ref writer);
        LocalWire.WriteBlob(ref writer, Payload, FieldBounds.PayloadLength);
    }
}
