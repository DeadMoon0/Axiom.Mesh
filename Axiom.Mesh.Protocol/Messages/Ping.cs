using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>Ping</c> (13, SPEC.md §7.11): sent after 60 seconds without sending, in Connected after the own <c>Hello</c> and in
/// Established (§6.3). <see cref="Amp.Definition"/> sends it as its heartbeat and answers a received one with
/// <see cref="Pong"/>; the heartbeat's nonce counts up per definition.
/// </summary>
public sealed class Ping : MeshMessage, IWireMessage<Ping>
{
    /// <summary>A ping.</summary>
    /// <param name="nonce">Any value; the answering <see cref="Pong"/> echoes it.</param>
    public Ping(ulong nonce) => Nonce = nonce;

    /// <inheritdoc />
    public override ushort Id => MessageIds.Ping;

    /// <summary>The value the answer echoes.</summary>
    public ulong Nonce { get; }

    /// <summary>The answer, carrying the same nonce.</summary>
    /// <returns>A <see cref="Pong"/>.</returns>
    public Pong Answer() => new(Nonce);

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out Ping message)
    {
        message = new Ping(reader.UInt64BigEndian());
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer) => writer.UInt64BigEndian(Nonce);

    /// <inheritdoc />
    public override string ToString() => $"Ping {Nonce}";
}
