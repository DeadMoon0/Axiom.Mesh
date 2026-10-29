namespace Axiom.Mesh.Core;

/// <summary>The two rows of the retention table of SPEC.md §11.2.</summary>
internal enum RetentionRow
{
    /// <summary>The desktop row: every Version, Tree, ChunkList and Chunk; every Message and attachment.</summary>
    Everything = 0,

    /// <summary>The phone row: every Version, the newest Version's Tree and ChunkLists; every Message; the rest on demand.</summary>
    LatestOnly = 1,
}
