using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// An event whose message id is in the event range but unknown to this client (LOCAL-SPEC.md §1.5): an extension of a
/// newer Host. <see cref="LocalProtocol.ClientSide"/> reads and discards it; it never reaches the application.
/// </summary>
public sealed class UnknownEvent : LocalMessage, IHostMessage
{
    /// <summary>The message id, 1024 to 1535.</summary>
    public required ushort Id { get; init; }

    /// <summary>The subscription it was sent on.</summary>
    public required uint SubscriptionId { get; init; }

    /// <summary>The body as received, valid until the next message is read; empty unless set.</summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <inheritdoc />
    public override string ToString() => $"@{SubscriptionId} UnknownEvent({Id})";

    internal override void WritePayload(ref WireWriter writer)
    {
        if (LocalMessageIds.ClassOf(Id) != MessageClass.Event)
            throw new ArgumentException($"Id {Id} is not in the event range 1024 to 1535 (LOCAL-SPEC.md §3.3).", nameof(Id));
        writer.UInt32BigEndian(SubscriptionId);
        writer.Bytes(Body.Span);
    }
}
