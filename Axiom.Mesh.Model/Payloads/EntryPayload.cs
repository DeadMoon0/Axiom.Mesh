using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// An Entry's payload, by the Entry's kind (SPEC.md §8.5): <see cref="VersionPayload"/>, <see cref="MessagePayload"/>,
/// <see cref="RedactionPayload"/>, or <see cref="OpaquePayload"/> for a kind this implementation does not interpret.
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
    /// The objects the payload references, which the writer pins and a reader fetches (SPEC.md §10.3, §11.2;
    /// LOCAL-SPEC.md §9.4): a <c>Version</c>'s Tree, a <c>Message</c>'s attachments; none for a <c>Redaction</c> (its
    /// target is an Entry) or an opaque payload (only its writer can say).
    /// </summary>
    public abstract IReadOnlyList<ObjectHash> References { get; }

    /// <summary>
    /// Parses the payload of an Entry of <paramref name="kind"/>. A payload of a known kind must parse exactly to its
    /// end, else the Entry is malformed (§8.5); any other kind is opaque and always parses.
    /// </summary>
    /// <param name="kind">The Entry's kind.</param>
    /// <param name="bytes">The payload bytes; the result refers to them.</param>
    /// <param name="payload">The payload, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when a payload of a known kind does not parse.</returns>
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
}
