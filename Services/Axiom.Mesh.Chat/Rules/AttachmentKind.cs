namespace Axiom.Mesh.Chat;

/// <summary>What an attachment is stored as, or written back as (PAYLOADS.md §3).</summary>
internal enum AttachmentKind
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>A file, as a ChunkList (SPEC.md §8.3).</summary>
    File = 1,

    /// <summary>A folder, as a Tree (SPEC.md §8.4).</summary>
    Folder = 2,
}
