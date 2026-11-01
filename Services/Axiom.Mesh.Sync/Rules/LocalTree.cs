using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The state of a bound directory as a <see cref="FlatTree"/> (SYNC-SERVICE.md §3 step 5): every file by its fingerprint, every
/// directory the scan found (an empty one included), and, for each path the binding does not publish (ignored, a name the file
/// system refused, a file too large or unreadable, a symlink it does not write), the state the base had, so that leaving a path
/// alone never reads as deleting it. Pure.
/// </summary>
internal static class LocalTree
{
    /// <summary>The directory's state.</summary>
    /// <param name="files">The fingerprint of every file that stands for this run.</param>
    /// <param name="directories">Every directory the scan found, relative with <c>/</c>.</param>
    /// <param name="baseTree">What the directory last equalled.</param>
    /// <param name="carried">Whether a path of the base is one the binding leaves alone, kept as the base had it.</param>
    /// <returns>The state.</returns>
    public static FlatTree Of(IEnumerable<Fingerprint> files, IEnumerable<string> directories, FlatTree baseTree, Func<string, bool> carried)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentNullException.ThrowIfNull(baseTree);
        ArgumentNullException.ThrowIfNull(carried);
        var paths = new Dictionary<string, FlatEntry>(StringComparer.Ordinal);
        foreach (Fingerprint file in files)
            paths[file.Path] = file.Entry;
        foreach (string directory in directories)
            paths.TryAdd(directory, FlatEntry.Directory);

        foreach ((string path, FlatEntry entry) in baseTree.Entries)
        {
            if (paths.ContainsKey(path) || !carried(path) || BelowAFile(paths, path))
                continue;
            paths[path] = entry;
        }

        return FlatTree.Of(paths);
    }

    // A carried path cannot lie below a file the directory holds now.
    private static bool BelowAFile(Dictionary<string, FlatEntry> paths, string path)
    {
        for (int slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
        {
            if (paths.TryGetValue(path[..slash], out FlatEntry above) && !above.IsDirectory)
                return true;
        }

        return false;
    }
}
