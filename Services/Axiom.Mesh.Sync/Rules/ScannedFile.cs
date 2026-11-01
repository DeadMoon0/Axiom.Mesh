namespace Axiom.Mesh.Sync;

/// <summary>One regular file a scan found, as the file system states it; its bytes are not read.</summary>
/// <param name="Path">Its path relative to the bound directory, with <c>/</c>.</param>
/// <param name="Mode">File, or ExecutableFile where the file system has an executable bit and it is set.</param>
/// <param name="Size">Its length.</param>
/// <param name="WriteTicks">Its last write time, in UTC ticks.</param>
internal readonly record struct ScannedFile(string Path, TreeEntryMode Mode, ulong Size, long WriteTicks);
