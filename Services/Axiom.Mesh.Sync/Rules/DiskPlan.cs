using System.Collections.Generic;

namespace Axiom.Mesh.Sync;

/// <summary>
/// What writing a target state into a bound directory does (<see cref="DiskDiff.Of"/>), decided before a byte is written: the
/// paths to delete (the deepest first; a directory only once empty), the directories to create (parents first), the files and
/// symlinks to write (parents first), the paths left as they are because they changed here since the base, and the names this
/// file system cannot hold. Made only by <see cref="DiskDiff"/>.
/// </summary>
internal sealed class DiskPlan
{
    internal DiskPlan(IReadOnlyList<string> deletes, IReadOnlyList<string> directories, IReadOnlyList<KeyValuePair<string, FlatEntry>> writes, IReadOnlyList<string> skipped, IReadOnlyList<string> refused)
    {
        Deletes = deletes;
        Directories = directories;
        Writes = writes;
        Skipped = skipped;
        Refused = refused;
    }

    /// <summary>Paths to delete, the deepest first: files, symlinks written as files, and directories once nothing is left in them.</summary>
    public IReadOnlyList<string> Deletes { get; }

    /// <summary>Directories to create, parents first.</summary>
    public IReadOnlyList<string> Directories { get; }

    /// <summary>Files and symlinks to write, with the entry each becomes, parents first.</summary>
    public IReadOnlyList<KeyValuePair<string, FlatEntry>> Writes { get; }

    /// <summary>Paths the target changes that changed here since the base (or are being written now): left as they are.</summary>
    public IReadOnlyList<string> Skipped { get; }

    /// <summary>Paths whose name this file system cannot hold, or that collide case-insensitively: not written, a conflict each.</summary>
    public IReadOnlyList<string> Refused { get; }

    /// <summary>Whether nothing is written or deleted.</summary>
    public bool IsEmpty => Deletes.Count == 0 && Directories.Count == 0 && Writes.Count == 0;

    /// <inheritdoc />
    public override string ToString() => $"{Writes.Count} writes, {Directories.Count} directories, {Deletes.Count} deletes, {Skipped.Count} kept, {Refused.Count} refused";
}
