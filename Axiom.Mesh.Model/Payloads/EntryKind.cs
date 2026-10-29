namespace Axiom.Mesh;

/// <summary>
/// What an Entry's payload is (SPEC.md §8.5, §14.7). A kind this enumeration does not name is not malformed: the Entry
/// is stored, replicated and ordered, and its payload is opaque; the raw value is kept.
/// </summary>
public enum EntryKind : ushort
{
    /// <summary>Kind 0, assigned to nothing; its payload is opaque.</summary>
    Unassigned = 0,

    /// <summary>A version of an Archive's files (SPEC.md §8.5).</summary>
    Version = 1,

    /// <summary>A message in a Channel (SPEC.md §8.5).</summary>
    Message = 2,

    /// <summary>The withdrawal of an earlier Entry in a Channel (SPEC.md §8.5, §9.4).</summary>
    Redaction = 3,
}
