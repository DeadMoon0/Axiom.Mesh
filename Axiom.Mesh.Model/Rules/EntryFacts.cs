namespace Axiom.Mesh;

/// <summary>
/// What the rules about parents and redactions need of a stored Entry (SPEC.md §8.6, §9.4), so that they can be applied
/// to an Entry a node holds as an index row as well as to one it holds as an object.
/// </summary>
/// <param name="Package">The Entry's Package.</param>
/// <param name="Kind">The Entry's kind.</param>
/// <param name="Key">The Entry's clock, device and hash.</param>
[ClosedShape("The facts the rules of SPEC.md §8.6 and §9.4 read are fixed by the specification.")]
public readonly record struct EntryFacts(PackageId Package, EntryKind Kind, EntryKey Key);
