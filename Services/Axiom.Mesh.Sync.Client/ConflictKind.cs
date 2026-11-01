namespace Axiom.Mesh.Sync;

/// <summary>Why the service recorded a conflict (SYNC-SERVICE.md §4, §7).</summary>
public enum ConflictKind : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>A path changed differently on two leaves of the branch: the state of the later leaf stands, the copy of the other side lies beside it (SPEC.md §9.5).</summary>
    CopyWritten = 1,

    /// <summary>A name this file system cannot hold, or one that collides with another case-insensitively, was not written (SPEC.md §8.4).</summary>
    NameRefused = 2,

    /// <summary>A file larger than a ChunkList can name (32 GiB, SPEC.md §8.3) was not published.</summary>
    TooLarge = 3,

    /// <summary>A file could not be read, and was not published.</summary>
    Unreadable = 4,

    /// <summary>A directory holds more entries than a Tree may (8 192, SPEC.md §8.4); it was not published.</summary>
    TooManyEntries = 5,
}
