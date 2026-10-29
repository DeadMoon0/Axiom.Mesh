using System;

namespace Axiom.Mesh;

/// <summary>
/// An object that may be stored, served and forwarded (SPEC.md §8.1, §10.6, §11.1): its hash matched, its form parsed,
/// and, for a signed object, its signature verified against the key SPEC.md §8 names and, for an Entry, its writer
/// admitted. The store and the <c>Object</c> message accept nothing else.
/// </summary>
/// <remarks>
/// Its constructor is private to this package, and every subclass is sealed with an internal constructor: outside
/// Model a storable object is obtained from <see cref="Objects.TryParse"/> (<see cref="Chunk"/>, <see cref="ChunkList"/>,
/// <see cref="Tree"/>, <see cref="OpaqueObject"/>), from <see cref="VerifiedManifest.TryVerify"/>,
/// <see cref="VerifiedRoster.TryVerify"/> and <see cref="VerifiedGrant.TryVerify"/>, or from
/// <see cref="EntryAdmission.Decide"/> (<see cref="AdmittedEntry"/>), and from nowhere else.
/// </remarks>
public abstract class StorableObject : ParsedObject
{
    private protected StorableObject(ObjectHash hash, ReadOnlyMemory<byte> bytes)
        : base(hash, bytes)
    {
    }
}
