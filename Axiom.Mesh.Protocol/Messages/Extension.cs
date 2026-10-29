using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// A frame whose id is reserved for AMP/1 extensions (17 to 255) and unknown to this version: read and discarded
/// (SPEC.md §1.4). Its body is not kept. <see cref="Amp.Definition"/> consumes one silently in Established; before the
/// peer's <c>Hello</c> it is a violation like any frame but <c>Hello</c> and <c>Bye</c> (§6.1).
/// </summary>
public sealed class Extension : MeshMessage
{
    /// <summary>An extension frame with an empty body.</summary>
    /// <param name="id">The message id, 17 to 255.</param>
    /// <exception cref="ArgumentOutOfRangeException">The id is outside the extension range.</exception>
    public Extension(ushort id)
    {
        if (id is < MessageIds.FirstExtension or > MessageIds.LastExtension)
            throw new ArgumentOutOfRangeException(nameof(id), id, $"Extension ids are {MessageIds.FirstExtension} to {MessageIds.LastExtension} (SPEC.md §14.1).");
        Id = id;
    }

    /// <inheritdoc />
    public override ushort Id { get; }

    /// <summary>Writes nothing: an unknown extension carries no body this node could know.</summary>
    /// <param name="writer">The writer.</param>
    public override void Write(ref WireWriter writer)
    {
    }

    /// <inheritdoc />
    public override string ToString() => $"Extension {Id}";
}
