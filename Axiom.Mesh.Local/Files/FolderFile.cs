namespace Axiom.Mesh.Local;

/// <summary>
/// One entry of a folder as <see cref="FolderTrees.TryBuild(System.Collections.Generic.IReadOnlyList{FolderFile}, out System.Collections.Generic.IReadOnlyList{Tree}, out string?)"/>
/// builds Trees from it (SPEC.md §8.4): a file, an executable file, a symlink, or an explicit directory.
/// </summary>
/// <param name="Path">Its path relative to the folder, names separated by <c>/</c>.</param>
/// <param name="Mode">File, ExecutableFile, Symlink, or Directory for a directory that must exist even when nothing lies in it.</param>
/// <param name="Hash">A ChunkList for a file, a Chunk holding the target for a symlink; zero for a directory.</param>
/// <param name="Size">The ChunkList's size, the Chunk's length; 0 for a directory.</param>
[ClosedShape("A folder entry is exactly a Tree entry's fields with its path in place of its name (SPEC.md §8.4).")]
public readonly record struct FolderFile(string Path, TreeEntryMode Mode, ObjectHash Hash, ulong Size);
