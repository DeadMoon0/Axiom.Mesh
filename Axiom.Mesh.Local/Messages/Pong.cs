using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>Pong</c> (5, LOCAL-SPEC.md §4.4): the answer to a <see cref="Ping"/>; one with an unknown nonce is ignored.</summary>
public sealed class Pong : LocalMessage, IClientMessage, IHostMessage
{
    /// <summary>The nonce of the <see cref="Ping"/> it answers.</summary>
    public ulong Nonce { get; init; }

    /// <inheritdoc />
    public override ushort MessageId => LocalMessageIds.Pong;

    /// <inheritdoc />
    public override string ToString() => $"Pong({Nonce})";

    internal static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Pong? message)
    {
        var read = new Pong { Nonce = reader.UInt64BigEndian() };
        message = reader.Ok ? read : null;
        return message is not null;
    }

    internal override void WritePayload(ref WireWriter writer) => writer.UInt64BigEndian(Nonce);
}
