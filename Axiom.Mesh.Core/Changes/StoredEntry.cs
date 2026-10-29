namespace Axiom.Mesh.Core;

/// <summary>An Entry stored by a batch, with its store sequence (LOCAL-SPEC.md §5.6): what the <c>entries</c> topic carries.</summary>
/// <param name="Hash">The Entry's hash.</param>
/// <param name="StoreSeq">Its store sequence.</param>
/// <param name="Package">Its Package.</param>
/// <param name="Key">Its position in the total order.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Redacted">Whether a stored Redaction withdraws it.</param>
/// <param name="StoredMs">When it was stored, in milliseconds since the Unix epoch.</param>
/// <param name="Origin">Who sent or wrote it.</param>
internal sealed record StoredEntry(ObjectHash Hash, ulong StoreSeq, PackageId Package, EntryKey Key, EntryKind Kind, bool Redacted, long StoredMs, Origin Origin);
