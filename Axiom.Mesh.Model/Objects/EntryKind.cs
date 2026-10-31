namespace Axiom.Mesh;

/// <summary>
/// The Entry kinds of the registry of SPEC.md §14.7. A kind is service-defined and every payload is opaque to a node
/// (SPEC.md §8.5): a kind this enumeration does not name is stored, replicated and ordered like any other, and its raw
/// value is kept. The layouts of the kinds named here are in PAYLOADS.md (<c>Axiom.Mesh.Payloads</c>).
/// </summary>
public enum EntryKind : ushort
{
    /// <summary>Kind 0, assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>A version of an Archive's files (PAYLOADS.md §2).</summary>
    Version = 1,

    /// <summary>A message in a Channel (PAYLOADS.md §3).</summary>
    Message = 2,

    /// <summary>The withdrawal of an earlier Entry in a Channel (PAYLOADS.md §4; SPEC.md §9.4).</summary>
    Redaction = 3,
}
