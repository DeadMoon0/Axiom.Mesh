namespace Axiom.Mesh;

/// <summary>Why an object is dropped rather than stored (SPEC.md §8.1, §12.2).</summary>
public enum ObjectFaultKind
{
    /// <summary>
    /// The bytes do not have the form SPEC.md §8 gives the object: a field outside its bounds, a list out of order, an
    /// id that does not derive, trailing bytes. Decided from the bytes alone.
    /// </summary>
    Malformed = 0,

    /// <summary>
    /// Well-formed, but wrong in substance once the node holds what it refers to: a parent of another Package, a clock
    /// that jumped, a Manifest of another kind, a ChunkList whose size is wrong (SPEC.md §12.2).
    /// </summary>
    Faulty = 1,
}
