namespace Axiom.Mesh;

/// <summary>One entry of a Tree (SPEC.md §8.4).</summary>
/// <param name="Name">The entry's name: not empty, not <c>.</c> or <c>..</c>, no <c>/</c>, <c>\</c> or NUL, at most 255 UTF-8 bytes.</param>
/// <param name="Mode">What the entry is.</param>
/// <param name="Hash">A ChunkList for a file, a Tree for a directory, a Chunk holding the target for a symlink.</param>
/// <param name="Size">A copy of the file's size or the link target's length for display; 0 for a directory.</param>
[ClosedShape("The layout of a Tree entry is fixed by SPEC.md §8.4.")]
public readonly record struct TreeEntry(string Name, TreeEntryMode Mode, ObjectHash Hash, ulong Size);
