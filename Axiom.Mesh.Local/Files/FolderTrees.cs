using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Local;

/// <summary>
/// The Trees of a folder (SPEC.md §8.4), from its entries: one Tree per directory, built from the deepest up, so every
/// directory entry names a Tree already made. Files, executable files and symlinks are entries of their directory's Tree; a
/// directory given explicitly exists even when nothing lies in it. Pure.
/// </summary>
public static class FolderTrees
{
    /// <summary>Builds the Trees of a folder from its entries.</summary>
    /// <param name="files">Every entry of the folder, with its path relative to it, in any order; each path once.</param>
    /// <param name="trees">Every Tree, the deepest first and the root last (an empty folder is one empty Tree); or empty.</param>
    /// <param name="problem">
    /// Why the folder makes no Tree: a name a Tree may not hold, more than 8 192 entries in one directory, a path given twice
    /// or both as a file and as a directory; or <see langword="null"/>.
    /// </param>
    /// <returns><see langword="true"/> when the Trees were built.</returns>
    public static bool TryBuild(IReadOnlyList<FolderFile> files, out IReadOnlyList<Tree> trees, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(files);
        var made = new List<Tree>();
        var level = new List<(string Remainder, FolderFile File)>(files.Count);
        foreach (FolderFile file in files)
            level.Add((file.Path ?? string.Empty, file));
        Tree? root = Directory(string.Empty, level, made, out problem);
        trees = root is null ? [] : made;
        return root is not null;
    }

    /// <summary>Builds the Trees of a folder from its regular files' ChunkLists, as <see cref="Trees.PublishDirectoryAsync"/> stores them.</summary>
    /// <param name="files">Every regular file of the folder, with its path relative to it.</param>
    /// <param name="trees">Every Tree, the root last; or empty.</param>
    /// <param name="problem">Why the folder makes no Tree, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the Trees were built.</returns>
    internal static bool TryBuild(IReadOnlyList<(string Path, StoredFile File)> files, out IReadOnlyList<Tree> trees, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(files);
        return TryBuild([.. files.Select(f => new FolderFile(f.Path, TreeEntryMode.File, f.File.ChunkList, f.File.Size))], out trees, out problem);
    }

    // The Tree of one directory: its files, and the Trees of its subdirectories made first.
    private static Tree? Directory(string prefix, List<(string Remainder, FolderFile File)> inside, List<Tree> made, out string? problem)
    {
        problem = null;
        var entries = new List<TreeEntry>();
        var directories = new Dictionary<string, List<(string Remainder, FolderFile File)>>(StringComparer.Ordinal);
        var leaves = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string remainder, FolderFile file) in inside)
        {
            int slash = remainder.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0 && file.Mode != TreeEntryMode.Directory)
            {
                if (!leaves.Add(remainder))
                {
                    problem = $"{prefix}{remainder} is given twice.";
                    return null;
                }

                entries.Add(new TreeEntry(remainder, file.Mode, file.Hash, file.Size));
                continue;
            }

            string name = slash < 0 ? remainder : remainder[..slash];
            if (!directories.TryGetValue(name, out List<(string, FolderFile)>? below))
                directories[name] = below = [];
            if (slash >= 0)
                below.Add((remainder[(slash + 1)..], file));
        }

        foreach ((string name, List<(string Remainder, FolderFile File)> below) in directories.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            if (leaves.Contains(name))
            {
                problem = $"{prefix}{name} is both a file and a directory.";
                return null;
            }

            Tree? sub = Directory(prefix + name + "/", below, made, out problem);
            if (sub is null)
                return null;
            entries.Add(new TreeEntry(name, TreeEntryMode.Directory, sub.Hash, 0));
        }

        if (!ObjectBuilders.TryTree(entries, out Tree? tree, out problem))
        {
            problem = prefix.Length == 0 ? problem : $"{prefix.TrimEnd('/')}: {problem}";
            return null;
        }

        made.Add(tree);
        return tree;
    }
}
