namespace Axiom.Mesh;

/// <summary>
/// The first byte of every object (SPEC.md §8.1, §14.6). A tag this enumeration does not name is an extension's (or
/// the reserved sealed object's, 8): such an object is opaque, verified by its hash only (§1.4), and its tag is kept
/// as the raw value.
/// </summary>
public enum ObjectTag : byte
{
    /// <summary>Tag 0, assigned to nothing; an object with it is opaque.</summary>
    Unassigned = 0,

    /// <summary>File contents, 1 byte to 1 MiB (SPEC.md §8.2).</summary>
    Chunk = 1,

    /// <summary>A file: its size and its Chunks in order (SPEC.md §8.3).</summary>
    ChunkList = 2,

    /// <summary>A directory listing (SPEC.md §8.4).</summary>
    Tree = 3,

    /// <summary>One signed step of a Package's history (SPEC.md §8.6).</summary>
    Entry = 4,

    /// <summary>A Package's owner-signed description (SPEC.md §8.7).</summary>
    Manifest = 5,

    /// <summary>A user's signed list of devices (SPEC.md §8.8).</summary>
    Roster = 6,

    /// <summary>An owner's signed statement of a user's rights in a Package (SPEC.md §8.9).</summary>
    Grant = 7,
}
