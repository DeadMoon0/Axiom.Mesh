using System;

namespace Axiom.Mesh;

/// <summary>
/// An Entry that verified and whose device was, when it was decided, a not-removed device of the owner or of a current
/// <c>Write</c> grantee (SPEC.md §8.6, §9.6): storable. Once stored it stays valid whatever later Rosters and Grants
/// say. Made only by <see cref="EntryAdmission.Decide"/>.
/// </summary>
public sealed class AdmittedEntry : StorableObject
{
    internal AdmittedEntry(ObjectHash hash, ReadOnlyMemory<byte> bytes, EntryContent content)
        : base(hash, bytes) => Content = content;

    /// <summary>What the bytes say; signed by a device entitled to write.</summary>
    public EntryContent Content { get; }

    /// <inheritdoc />
    public override ObjectTag Tag => ObjectTag.Entry;
}
