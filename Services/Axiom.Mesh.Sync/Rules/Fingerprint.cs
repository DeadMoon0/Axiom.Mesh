namespace Axiom.Mesh.Sync;

/// <summary>
/// What the service knows of one file of a bound directory: its size and write time when it was last read, and the entry its
/// bytes became (a ChunkList, or the Chunk of a symlink the service wrote as a file). A file whose size and write time still
/// match is not read again.
/// </summary>
/// <param name="Path">Its path relative to the bound directory, with <c>/</c>.</param>
/// <param name="Mode">File, ExecutableFile, or Symlink for a link of the branch written as a file.</param>
/// <param name="Size">Its length when it was read or written.</param>
/// <param name="WriteTicks">Its last write time then, in UTC ticks.</param>
/// <param name="Hash">The ChunkList, or the Chunk of a symlink.</param>
/// <param name="TakenTicks">When the file was read for it, in UTC ticks; <see cref="long.MaxValue"/> for a file the service wrote itself.</param>
internal readonly record struct Fingerprint(string Path, TreeEntryMode Mode, ulong Size, long WriteTicks, ObjectHash Hash, long TakenTicks)
{
    /// <summary>The entry the file is in a Tree.</summary>
    public FlatEntry Entry => new(Mode, Hash, Size);
}
