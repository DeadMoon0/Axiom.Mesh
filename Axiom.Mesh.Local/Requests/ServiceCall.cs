using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ServiceCall</c> (385, LOCAL-SPEC.md §6.7): Forwards a call to a service as a <c>ForwardedCall</c>; the OK body is the service's reply payload.</summary>
public sealed record ServiceCall : LocalRequest, IRequest<ServiceCall, ReadOnlyMemory<byte>>
{
    /// <summary>The message id, 385 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 385;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ServiceCall";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.7).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The service.</summary>
    public required string Service { get; init; }

    /// <summary>The service's method.</summary>
    public required string Method { get; init; }

    /// <summary>The call's payload, the service's own.</summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out ReadOnlyMemory<byte> ok)
    {
        ok = LocalWire.ReadBlob(ref reader, FieldBounds.PayloadLength);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, ReadOnlyMemory<byte> ok)
    {
        LocalWire.WriteBlob(ref writer, ok, FieldBounds.PayloadLength);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ServiceCall? value)
    {
        var read = new ServiceCall
        {
            Service = Wire.ReadString(ref reader, FieldBounds.NameLength),
            Method = Wire.ReadString(ref reader, FieldBounds.NameLength),
            Payload = LocalWire.ReadBlob(ref reader, FieldBounds.PayloadLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, Service, FieldBounds.NameLength);
        Wire.WriteString(ref writer, Method, FieldBounds.NameLength);
        LocalWire.WriteBlob(ref writer, Payload, FieldBounds.PayloadLength);
    }
}
