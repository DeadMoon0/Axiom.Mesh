using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Sync;

/// <summary>
/// A Tree and every Tree below it as one map of paths (SPEC.md §8.4): every file, symlink and directory under its path relative
/// to the root with <c>/</c>, so that two states of a folder compare path by path. Every directory is a path of its own, an empty
/// one included, so rebuilding the Trees (<see cref="TreeSet"/>) gives the same root hash. Immutable.
/// </summary>
internal sealed class FlatTree
{
    private readonly SortedDictionary<string, FlatEntry> entries;

    private FlatTree(SortedDictionary<string, FlatEntry> entries) => this.entries = entries;

    /// <summary>The empty folder.</summary>
    public static FlatTree Empty { get; } = new(new SortedDictionary<string, FlatEntry>(StringComparer.Ordinal));

    /// <summary>Every path, in ordinal order.</summary>
    public IReadOnlyDictionary<string, FlatEntry> Entries => entries;

    /// <summary>How many paths it holds.</summary>
    public int Count => entries.Count;

    /// <summary>The files and symlinks: every path that is not a directory.</summary>
    public IEnumerable<KeyValuePair<string, FlatEntry>> Files => entries.Where(e => !e.Value.IsDirectory);

    /// <summary>A folder of these paths; every directory a path lies in is added.</summary>
    /// <param name="paths">The paths and their entries; each path once.</param>
    /// <returns>The folder.</returns>
    /// <exception cref="ArgumentException">A path is given twice, or lies below a file.</exception>
    public static FlatTree Of(IEnumerable<KeyValuePair<string, FlatEntry>> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var built = new SortedDictionary<string, FlatEntry>(StringComparer.Ordinal);
        foreach ((string path, FlatEntry entry) in paths)
        {
            if (built.TryGetValue(path, out FlatEntry existing) && !(existing.IsDirectory && entry.IsDirectory))
                throw new ArgumentException($"The path {path} is given twice.", nameof(paths));
            built[path] = entry;
            for (int slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
            {
                string parent = path[..slash];
                if (built.TryGetValue(parent, out FlatEntry above) && !above.IsDirectory)
                    throw new ArgumentException($"The path {path} lies below the file {parent}.", nameof(paths));
                if (!built.TryAdd(parent, FlatEntry.Directory))
                    break;
            }
        }

        return new FlatTree(built);
    }

    /// <summary>The entry at a path.</summary>
    /// <param name="path">The path.</param>
    /// <param name="entry">The entry.</param>
    /// <returns><see langword="false"/> when nothing is there.</returns>
    public bool TryGet(string path, out FlatEntry entry) => entries.TryGetValue(path, out entry);

    /// <summary>The entry at a path, or <see langword="null"/>.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The entry.</returns>
    public FlatEntry? At(string path) => entries.TryGetValue(path, out FlatEntry entry) ? entry : null;

    /// <inheritdoc />
    public override string ToString() => $"{Count} paths, {Files.Count()} files";
}
