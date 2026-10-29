using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ForwardedCall</c> (1537, LOCAL-SPEC.md §9.8): a <c>ServiceCall</c> from a user client, forwarded verbatim; the service replies within 60 seconds with its own payload or a status.</summary>
public sealed record ForwardedCall : LocalCallback, ICallback<ForwardedCall, ReadOnlyMemory<byte>>
{
    /// <summary>The message id, 1537 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 1537;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The Host's number for the calling connection, for logs.</summary>
    public uint Client { get; init; }

    /// <summary>The caller's <c>Hello.name</c>.</summary>
    public string ClientName { get; init; } = "";

    /// <summary>The service's method.</summary>
    public string Method { get; init; } = "";

    /// <summary>The call's payload.</summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <inheritdoc />
    public static bool TryReadReply(ref WireReader reader, [MaybeNullWhen(false)] out ReadOnlyMemory<byte> reply)
    {
        reply = LocalWire.ReadBlob(ref reader, FieldBounds.PayloadLength);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteReply(ref WireWriter writer, ReadOnlyMemory<byte> reply) =>
        LocalWire.WriteBlob(ref writer, reply, FieldBounds.PayloadLength);

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ForwardedCall? value)
    {
        var read = new ForwardedCall
        {
            Client = reader.UInt32BigEndian(),
            ClientName = Wire.ReadString(ref reader, FieldBounds.NameLength),
            Method = Wire.ReadString(ref reader, FieldBounds.NameLength),
            Payload = LocalWire.ReadBlob(ref reader, FieldBounds.PayloadLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt32BigEndian(Client);
        Wire.WriteString(ref writer, ClientName, FieldBounds.NameLength);
        Wire.WriteString(ref writer, Method, FieldBounds.NameLength);
        LocalWire.WriteBlob(ref writer, Payload, FieldBounds.PayloadLength);
    }
}
