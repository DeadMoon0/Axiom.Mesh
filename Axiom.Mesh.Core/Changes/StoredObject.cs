namespace Axiom.Mesh.Core;

/// <summary>An object stored by a batch: what sessions announce with <c>Have</c> (SPEC.md §10.5).</summary>
/// <param name="Hash">The object's hash.</param>
/// <param name="Tag">The object's tag byte.</param>
/// <param name="Size">Its length in bytes.</param>
/// <param name="Origin">Who sent it; it is not announced back there.</param>
/// <param name="Context">The Package it was wanted or written for, when known.</param>
internal sealed record StoredObject(ObjectHash Hash, byte Tag, long Size, Origin Origin, PackageId? Context);
