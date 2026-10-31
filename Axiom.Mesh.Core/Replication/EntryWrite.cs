namespace Axiom.Mesh.Core;

/// <summary>
/// What an Entry written on this device says (LOCAL-SPEC.md §9.4): the Package, the body (kind, opaque payload, the
/// objects it references and the Entry it redacts, all copied into the envelope as given) and the parents the writer
/// named (none for the Host's choice of SPEC.md §9.3). The Host parses no payload.
/// </summary>
/// <param name="Package">The Package.</param>
/// <param name="Body">What the Entry says.</param>
/// <param name="Parents">The parents named; empty for the Host's choice.</param>
internal sealed record EntryWrite(PackageId Package, EntryBody Body, EntryParents Parents);
