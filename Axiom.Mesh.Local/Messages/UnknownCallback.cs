using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// A callback whose message id is in the callback range but unknown to this service (LOCAL-SPEC.md §1.5), the reserved
/// 1536 included. <see cref="LocalProtocol.ClientSide"/> answers it with <c>Reply(UnknownRequest)</c> on its own.
/// </summary>
public sealed class UnknownCallback : LocalMessage, IHostMessage
{
    /// <summary>The message id, 1536 to 2047.</summary>
    public required ushort Id { get; init; }

    /// <summary>The callback id, never 0.</summary>
    public required uint CallbackId { get; init; }

    /// <summary>The body as received, valid until the next message is read; empty unless set.</summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <inheritdoc />
    public override string ToString() => $"!{CallbackId} UnknownCallback({Id})";

    internal override void WritePayload(ref WireWriter writer)
    {
        if (LocalMessageIds.ClassOf(Id) != MessageClass.Callback)
            throw new ArgumentException($"Id {Id} is not in the callback range 1536 to 2047 (LOCAL-SPEC.md §3.3).", nameof(Id));
        if (CallbackId == 0)
            throw new ArgumentException("A callback id is never 0 (LOCAL-SPEC.md §3.2).", nameof(CallbackId));
        writer.UInt32BigEndian(CallbackId);
        writer.Bytes(Body.Span);
    }
}
