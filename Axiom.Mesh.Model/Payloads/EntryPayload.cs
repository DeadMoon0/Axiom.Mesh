using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Payloads;

/// <summary>
/// An Entry's payload in one of the layouts of PAYLOADS.md, by the Entry's kind: <see cref="VersionPayload"/>,
/// <see cref="MessagePayload"/>, <see cref="RedactionPayload"/>, or <see cref="OpaquePayload"/> for a kind without a
/// layout here. For services and the command line: a node never parses a payload (SPEC.md §8.5); what it acts on is the
/// Entry's envelope, which <see cref="ToBody"/> fills from the payload.
/// </summary>
/// <remarks>A closed set: no type outside this package derives from it.</remarks>
public abstract class EntryPayload
{
    private protected EntryPayload()
    {
    }

    /// <summary>The Entry kind this payload belongs to.</summary>
    public abstract EntryKind Kind { get; }

    /// <summary>
    /// The objects the payload names, which belong in the Entry's <c>references</c> (SPEC.md §8.6): a <c>Version</c>'s
    /// Tree, a <c>Message</c>'s attachments; none for a <c>Redaction</c> (its target is an Entry) or an opaque payload
    /// (only its writer can say).
    /// </summary>
    public abstract IReadOnlyList<ObjectHash> References { get; }

    /// <summary>The Entry the payload withdraws, which belongs in the Entry's <c>redacts</c>; zero for all but a <c>Redaction</c>.</summary>
    public virtual ObjectHash Redacts => default;

    /// <summary>
    /// Parses the payload of an Entry of <paramref name="kind"/>, the service's own check. A payload of a kind with a
    /// layout must parse exactly to its end; any other kind is opaque and always parses.
    /// </summary>
    /// <param name="kind">The Entry's kind.</param>
    /// <param name="bytes">The payload bytes; the result refers to them.</param>
    /// <param name="payload">The payload, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when a payload of a kind with a layout does not parse.</returns>
    public static bool TryParse(EntryKind kind, ReadOnlyMemory<byte> bytes, [NotNullWhen(true)] out EntryPayload? payload)
    {
        var reader = new WireReader(bytes);
        payload = kind switch
        {
            EntryKind.Version => VersionPayload.Read(ref reader),
            EntryKind.Message => MessagePayload.Read(ref reader),
            EntryKind.Redaction => RedactionPayload.Read(ref reader),
            _ => new OpaquePayload(kind, bytes),
        };

        if (payload is OpaquePayload || (reader.Ok && reader.IsAtEnd))
            return true;

        payload = null;
        return false;
    }

    /// <summary>
    /// The Entry body that carries this payload: its kind, its encoding, and the envelope fields the node acts on,
    /// <c>references</c> from <see cref="References"/> and <c>redacts</c> from <see cref="Redacts"/>.
    /// </summary>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentException">A field is outside its bounds, the encoding exceeds 1 MiB, or more than 64 objects are named.</exception>
    public EntryBody ToBody() => new(Kind, PayloadEncoder.Encode(this), EntryReferences.Of(References), Redacts);
}
