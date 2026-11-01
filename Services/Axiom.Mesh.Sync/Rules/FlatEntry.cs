namespace Axiom.Mesh.Sync;

/// <summary>
/// One path of a <see cref="FlatTree"/>: a file, an executable file or a symlink with its object and size (SPEC.md §8.4), or a
/// directory, which carries no hash here: a directory is the same directory whatever lies in it.
/// </summary>
/// <param name="Mode">The Tree entry's mode.</param>
/// <param name="Hash">A ChunkList for a file, a Chunk for a symlink; zero for a directory.</param>
/// <param name="Size">The stated size; 0 for a directory.</param>
internal readonly record struct FlatEntry(TreeEntryMode Mode, ObjectHash Hash, ulong Size)
{
    /// <summary>A directory.</summary>
    public static FlatEntry Directory => new(TreeEntryMode.Directory, default, 0);

    /// <summary>Whether it is a directory.</summary>
    public bool IsDirectory => Mode == TreeEntryMode.Directory;
}
