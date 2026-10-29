using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>NotFound</c> (7, SPEC.md §7.7), Established only: the answer to a wanted hash the sender cannot or may not send;
/// the two cases are deliberately indistinguishable.
/// </summary>
public sealed class NotFound : MeshMessage, IWireMessage<NotFound>
{
    /// <summary>The answer for one wanted hash.</summary>
    /// <param name="hash">The hash that was wanted.</param>
    public NotFound(ObjectHash hash) => Hash = hash;

    /// <inheritdoc />
    public override ushort Id => MessageIds.NotFound;

    /// <summary>The hash that was wanted.</summary>
    public ObjectHash Hash { get; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out NotFound message)
    {
        message = new NotFound(ObjectHash.Read(ref reader));
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer) => Hash.Write(ref writer);

    /// <inheritdoc />
    public override string ToString() => $"NotFound {Hash}";
}
