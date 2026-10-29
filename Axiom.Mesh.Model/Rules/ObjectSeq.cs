namespace Axiom.Mesh;

/// <summary>
/// What decides which of several valid Manifests, Rosters or Grants is current: the sequence number, then the hash
/// (SPEC.md §8.7, §8.8, §8.9). See <see cref="CurrentObject.Supersedes"/>.
/// </summary>
/// <param name="Seq">The object's <c>seq</c>.</param>
/// <param name="Hash">The object's hash.</param>
[ClosedShape("Currency is decided by exactly these two keys (SPEC.md §8.7–§8.9).")]
public readonly record struct ObjectSeq(ulong Seq, ObjectHash Hash);
