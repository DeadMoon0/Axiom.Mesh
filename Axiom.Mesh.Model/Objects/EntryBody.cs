using System;

namespace Axiom.Mesh;

/// <summary>
/// What a writer says in an Entry (SPEC.md §8.6), apart from where it sits in history: its <see cref="Kind"/>, its
/// opaque <see cref="Payload"/>, the objects it <see cref="References"/> and the Entry it <see cref="Redacts"/>. The node
/// acts on the last two and never parses the payload; a service builds the payload with its own encoder (for the kinds
/// of SPEC.md §14.7, <c>Axiom.Mesh.Payloads</c>).
/// </summary>
public sealed class EntryBody
{
    /// <summary>A body.</summary>
    /// <param name="kind">The Entry kind (SPEC.md §14.7); any value, service-defined.</param>
    /// <param name="payload">The payload bytes, opaque to the node; at most 1 MiB.</param>
    /// <param name="references">The objects the Entry needs.</param>
    /// <param name="redacts">The Entry this one withdraws (SPEC.md §9.4), or the zero hash for none.</param>
    /// <exception cref="ArgumentException">The payload is longer than 1 MiB.</exception>
    public EntryBody(EntryKind kind, ReadOnlyMemory<byte> payload, EntryReferences references, ObjectHash redacts)
    {
        if (payload.Length > Limits.MaxPayloadLength)
            throw new ArgumentException($"The payload is {payload.Length} bytes; at most {Limits.MaxPayloadLength} are allowed (SPEC.md §8.6).", nameof(payload));

        Kind = kind;
        Payload = payload;
        References = references;
        Redacts = redacts;
    }

    /// <summary>The Entry kind.</summary>
    public EntryKind Kind { get; }

    /// <summary>The payload bytes.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>The objects the Entry needs, ascending.</summary>
    public EntryReferences References { get; }

    /// <summary>The Entry withdrawn, or the zero hash.</summary>
    public ObjectHash Redacts { get; }

    /// <summary>Whether the Entry is a redaction (a non-zero <see cref="Redacts"/>), whatever its kind.</summary>
    public bool IsRedaction => !Redacts.IsZero;
}
