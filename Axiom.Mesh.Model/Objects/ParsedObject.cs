using System;

namespace Axiom.Mesh;

/// <summary>
/// An object whose bytes have the form SPEC.md §8 gives its tag: the second state of the object typestate, made only by
/// <see cref="Objects.TryParse"/>.
/// </summary>
/// <remarks>
/// The unsigned objects (<see cref="Chunk"/>, <see cref="ChunkList"/>, <see cref="Tree"/>, <see cref="OpaqueObject"/>)
/// need nothing but their hash and are <see cref="StorableObject"/>s as parsed. A signed object comes out as
/// <see cref="UnverifiedEntry"/>, <see cref="UnverifiedManifest"/>, <see cref="UnverifiedRoster"/> or
/// <see cref="UnverifiedGrant"/>, and is storable only once verified. No type outside this package can derive from it.
/// </remarks>
public abstract class ParsedObject
{
    private protected ParsedObject(ObjectHash hash, ReadOnlyMemory<byte> bytes)
    {
        Hash = hash;
        Bytes = bytes;
    }

    /// <summary>The object's name, <c>H(Bytes)</c>.</summary>
    public ObjectHash Hash { get; }

    /// <summary>Every byte of the object, as received; see <see cref="HashedObject"/> on their lifetime.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>The object's tag; an opaque object's raw tag, which this enumeration does not name.</summary>
    public abstract ObjectTag Tag { get; }
}
