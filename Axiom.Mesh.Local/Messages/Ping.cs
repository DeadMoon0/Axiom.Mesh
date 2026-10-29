using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// <c>Ping</c> (4, LOCAL-SPEC.md §4.4): the Host sends it after 60 seconds without receiving, and the client answers
/// <see cref="Pong"/> with the same nonce within 30 seconds; a client may ping the Host at any time. Both
/// definitions answer a <c>Ping</c> on their own.
/// </summary>
public sealed class Ping : LocalMessage, IClientMessage, IHostMessage
{
    /// <summary>The nonce the <see cref="Pong"/> carries back.</summary>
    public ulong Nonce { get; init; }

    /// <inheritdoc />
    public override ushort MessageId => LocalMessageIds.Ping;

    /// <summary>The answer to this ping.</summary>
    /// <returns>A <see cref="Pong"/> with the same nonce.</returns>
    public Pong Answer() => new() { Nonce = Nonce };

    /// <inheritdoc />
    public override string ToString() => $"Ping({Nonce})";

    internal static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Ping? message)
    {
        var read = new Ping { Nonce = reader.UInt64BigEndian() };
        message = reader.Ok ? read : null;
        return message is not null;
    }

    internal override void WritePayload(ref WireWriter writer) => writer.UInt64BigEndian(Nonce);
}
