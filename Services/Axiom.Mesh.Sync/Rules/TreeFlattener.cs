using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Sync;

/// <summary>Flattens a Tree and the Trees below it into a <see cref="FlatTree"/> (SPEC.md §8.4). Pure.</summary>
internal static class TreeFlattener
{
    /// <summary>The paths of a Tree.</summary>
    /// <param name="root">The root Tree.</param>
    /// <param name="trees">Every Tree below it, by hash.</param>
    /// <param name="flat">The paths, or <see cref="FlatTree.Empty"/>.</param>
    /// <param name="missing">The first Tree not at hand, or zero.</param>
    /// <returns><see langword="false"/> when a Tree below the root is not in <paramref name="trees"/>.</returns>
    public static bool TryOf(Tree root, IReadOnlyDictionary<ObjectHash, Tree> trees, out FlatTree flat, out ObjectHash missing)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(trees);
        var paths = new List<KeyValuePair<string, FlatEntry>>();
        var pending = new Stack<(string Prefix, Tree Tree)>([(string.Empty, root)]);
        missing = default;
        while (pending.TryPop(out (string Prefix, Tree Tree) next))
        {
            foreach (TreeEntry entry in next.Tree.Entries)
            {
                string path = next.Prefix + entry.Name;
                if (entry.Mode != TreeEntryMode.Directory)
                {
                    paths.Add(new(path, new FlatEntry(entry.Mode, entry.Hash, entry.Size)));
                    continue;
                }

                paths.Add(new(path, FlatEntry.Directory));
                if (!trees.TryGetValue(entry.Hash, out Tree? child))
                {
                    missing = entry.Hash;
                    flat = FlatTree.Empty;
                    return false;
                }

                pending.Push((path + "/", child));
            }
        }

        flat = FlatTree.Of(paths);
        return true;
    }
}
