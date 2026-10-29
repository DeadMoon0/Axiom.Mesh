using System;
using System.Collections.Generic;

namespace Axiom.Mesh;

/// <summary>
/// The payload of an Entry kind this implementation does not interpret (SPEC.md §8.5): carried as bytes, never parsed.
/// </summary>
public sealed class OpaquePayload : EntryPayload
{
    /// <summary>A payload of an extension kind.</summary>
    /// <param name="kind">The Entry kind; never one of the built-in kinds 1 to 3, whose payloads have their own types.</param>
    /// <param name="bytes">The payload bytes; at most 1 MiB.</param>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is a built-in kind.</exception>
    public OpaquePayload(EntryKind kind, ReadOnlyMemory<byte> bytes)
    {
        if (kind is EntryKind.Version or EntryKind.Message or EntryKind.Redaction)
            throw new ArgumentException($"Kind {kind} has a defined layout (SPEC.md §8.5); use its payload type, not an opaque one.", nameof(kind));

        Kind = kind;
        Bytes = bytes;
    }

    /// <inheritdoc />
    public override EntryKind Kind { get; }

    /// <summary>The payload bytes.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <inheritdoc />
    public override IReadOnlyList<ObjectHash> References => [];
}
