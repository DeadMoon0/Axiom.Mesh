using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Local;

/// <summary>
/// The Trees of a folder (SPEC.md §8.4), from its files' ChunkLists: one Tree per directory, built from the deepest up,
/// so every directory entry names a Tree already made. Pure.
/// </summary>
internal static class FolderTrees
{
    /// <summary>Builds the Trees of a folder.</summary>
    /// <param name="files">Every regular file of the folder, with its path relative to it.</param>
    /// <param name="trees">Every Tree, the root last; or empty.</param>
    /// <param name="problem">Why the folder makes no Tree (a name a Tree may not hold), or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the Trees were built.</returns>
    public static bool TryBuild(IReadOnlyList<(string Path, StoredFile File)> files, out IReadOnlyList<Tree> trees, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(files);
        var made = new List<Tree>();
        Tree? root = Directory(string.Empty, files, made, out problem);
        trees = root is null ? [] : made;
        return root is not null;
    }

    // The Tree of one directory: its files, and the Trees of its subdirectories made first.
    private static Tree? Directory(string prefix, IReadOnlyList<(string Path, StoredFile File)> files, List<Tree> made, out string? problem)
    {
        var entries = new List<TreeEntry>();
        var inside = files.Where(f => f.Path.StartsWith(prefix, StringComparison.Ordinal)).Select(f => (Remainder: f.Path[prefix.Length..], f.File)).ToList();
        foreach (var file in inside.Where(f => !f.Remainder.Contains('/', StringComparison.Ordinal)))
            entries.Add(new TreeEntry(file.Remainder, TreeEntryMode.File, file.File.ChunkList, file.File.Size));

        foreach (string directory in inside.Where(f => f.Remainder.Contains('/', StringComparison.Ordinal)).Select(f => f.Remainder[..f.Remainder.IndexOf('/', StringComparison.Ordinal)]).Distinct(StringComparer.Ordinal))
        {
            Tree? sub = Directory(prefix + directory + "/", files, made, out problem);
            if (sub is null)
                return null;
            entries.Add(new TreeEntry(directory, TreeEntryMode.Directory, sub.Hash, 0));
        }

        if (!ObjectBuilders.TryTree(entries, out Tree? tree, out problem))
            return null;
        made.Add(tree);
        return tree;
    }
}
