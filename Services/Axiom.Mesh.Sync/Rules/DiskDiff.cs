using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync;

/// <summary>
/// What writing a target state into a bound directory does (SYNC-SERVICE.md §3 step 11), path by path against the base the
/// directory last equalled and its state now:
/// <list type="bullet">
/// <item>a path the directory already holds as the target has it is left alone, so a resumed run writes only what is left;</item>
/// <item>a path the target did not change from the base is left alone, whatever the directory holds there (that is a local
/// change, the next run publishes it);</item>
/// <item>a path the target changed is written, or deleted, only when the directory still holds it as the base had it: a file
/// changed here since the base, or still being written, is never overwritten or deleted by a pull;</item>
/// <item>a name this file system cannot hold, or a case-insensitive collision on Windows, is refused (SPEC.md §8.4), with
/// everything below it;</item>
/// <item>a directory is deleted only when nothing is left in it, and a file replaces a directory only when everything in it
/// goes.</item>
/// </list>
/// Pure.
/// </summary>
internal static class DiskDiff
{
    /// <summary>Plans writing a target state.</summary>
    /// <param name="baseTree">What the directory last equalled.</param>
    /// <param name="target">What it is to become.</param>
    /// <param name="local">What it holds now.</param>
    /// <param name="busy">Paths being written now (changed within the stability time).</param>
    /// <param name="windows">The file system is Windows'.</param>
    /// <returns>The plan.</returns>
    public static DiskPlan Of(FlatTree baseTree, FlatTree target, FlatTree local, IReadOnlySet<string> busy, bool windows)
    {
        ArgumentNullException.ThrowIfNull(baseTree);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(busy);
        var deletes = new HashSet<string>(StringComparer.Ordinal);
        var directories = new List<string>();
        var writes = new List<KeyValuePair<string, FlatEntry>>();
        var skipped = new List<string>();
        var refused = new List<string>();
        var siblings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var overDirectories = new List<string>();

        foreach ((string path, FlatEntry wanted) in target.Entries)
        {
            if (refused.Any(r => path.StartsWith(r + "/", StringComparison.Ordinal)))
                continue;
            if (!FileNameRule.MayWrite(NameOf(path), windows) || (windows && !SiblingsOf(siblings, path).Add(NameOf(path))))
            {
                refused.Add(path);
                continue;
            }

            FlatEntry? here = local.At(path);
            FlatEntry? before = baseTree.At(path);
            if (here == wanted || before == wanted)
                continue;
            if (here != before || busy.Contains(path))
            {
                skipped.Add(path);
                continue;
            }

            if (wanted.IsDirectory)
            {
                if (here is { IsDirectory: false })
                    deletes.Add(path);
                directories.Add(path);
                continue;
            }

            writes.Add(new(path, wanted));
            if (here is { IsDirectory: true })
                overDirectories.Add(path);
        }

        foreach ((string path, FlatEntry before) in baseTree.Entries.Where(e => !target.TryGet(e.Key, out _)).OrderByDescending(e => Depth(e.Key)).ThenByDescending(e => e.Key, StringComparer.Ordinal))
        {
            if (local.At(path) is not { } here)
                continue;
            if (before.IsDirectory)
            {
                if (here.IsDirectory && EverythingBelowGoes(local, path, deletes))
                    deletes.Add(path);
                continue;
            }

            if (here == before && !busy.Contains(path))
                deletes.Add(path);
            else
                skipped.Add(path);
        }

        // A file replaces a directory only when everything in the directory goes; else the directory stays and the file waits.
        foreach (string path in overDirectories)
        {
            if (EverythingBelowGoes(local, path, deletes))
            {
                deletes.Add(path);
            }
            else
            {
                writes.RemoveAll(w => w.Key == path);
                skipped.Add(path);
            }
        }

        return new DiskPlan(
            [.. deletes.OrderByDescending(Depth).ThenByDescending(p => p, StringComparer.Ordinal)],
            directories,
            writes,
            [.. skipped.Order(StringComparer.Ordinal)],
            refused);
    }

    private static bool EverythingBelowGoes(FlatTree local, string directory, HashSet<string> deletes)
    {
        string prefix = directory + "/";
        return local.Entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).All(deletes.Contains);
    }

    private static HashSet<string> SiblingsOf(Dictionary<string, HashSet<string>> siblings, string path)
    {
        int slash = path.LastIndexOf('/');
        string parent = slash < 0 ? string.Empty : path[..slash];
        if (!siblings.TryGetValue(parent, out HashSet<string>? names))
            siblings[parent] = names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static int Depth(string path) => path.Count(c => c == '/');
}
