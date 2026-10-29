using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// A request whose message id is in the request range but not in <see cref="RequestTable"/> (LOCAL-SPEC.md §1.5, §3.4):
/// an extension of a newer client. It is never a violation: <see cref="LocalProtocol.HostSide"/> answers it with status
/// <c>UnknownRequest</c> on its own and the connection stays open. A client may send one to probe for an extension.
/// </summary>
public sealed class UnknownRequest : LocalMessage, IClientMessage
{
    /// <summary>The message id, 256 to 1023.</summary>
    public required ushort Id { get; init; }

    /// <summary>The correlation, never 0.</summary>
    public required uint Correlation { get; init; }

    /// <summary>The body as received, valid until the next message is read; empty unless set.</summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <inheritdoc />
    public override string ToString() => $"#{Correlation} UnknownRequest({Id})";

    internal override void WritePayload(ref WireWriter writer)
    {
        if (LocalMessageIds.ClassOf(Id) != MessageClass.Request)
            throw new ArgumentException($"Id {Id} is not in the request range 256 to 1023 (LOCAL-SPEC.md §3.3).", nameof(Id));
        if (Correlation == 0)
            throw new ArgumentException("A correlation is never 0 (LOCAL-SPEC.md §3.2).", nameof(Correlation));
        writer.UInt32BigEndian(Correlation);
        writer.Bytes(Body.Span);
    }
}
