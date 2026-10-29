using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>Pong</c> (14, SPEC.md §7.11): the answer to <see cref="Ping"/>, echoing its nonce. One with an unknown nonce is
/// ignored (§6.3); <see cref="Amp.Definition"/> consumes every received one, which still counts as receiving.
/// </summary>
public sealed class Pong : MeshMessage, IWireMessage<Pong>
{
    /// <summary>A pong.</summary>
    /// <param name="nonce">The nonce of the <see cref="Ping"/> it answers.</param>
    public Pong(ulong nonce) => Nonce = nonce;

    /// <inheritdoc />
    public override ushort Id => MessageIds.Pong;

    /// <summary>The nonce of the <see cref="Ping"/> it answers.</summary>
    public ulong Nonce { get; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out Pong message)
    {
        message = new Pong(reader.UInt64BigEndian());
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer) => writer.UInt64BigEndian(Nonce);

    /// <inheritdoc />
    public override string ToString() => $"Pong {Nonce}";
}
