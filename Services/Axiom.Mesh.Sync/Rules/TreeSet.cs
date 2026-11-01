using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The Trees of a <see cref="FlatTree"/> (SPEC.md §8.4), built from the deepest directory up: what a run puts before it writes a
/// Version naming <see cref="Root"/>. Flattening a Tree and building it again gives the same root hash. Made only by
/// <see cref="TryBuild"/>.
/// </summary>
internal sealed class TreeSet
{
    private TreeSet(IReadOnlyList<Tree> trees) => Trees = trees;

    /// <summary>The root Tree's hash.</summary>
    public ObjectHash Root => Trees[^1].Hash;

    /// <summary>Every Tree, the deepest first and the root last.</summary>
    public IReadOnlyList<Tree> Trees { get; }

    /// <summary>Builds the Trees of a folder.</summary>
    /// <param name="flat">The folder.</param>
    /// <param name="set">The Trees, or <see langword="null"/>.</param>
    /// <param name="problem">Why the folder makes no Tree (a directory of more than 8 192 entries, a name a Tree may not hold), or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the Trees were built.</returns>
    public static bool TryBuild(FlatTree flat, out TreeSet? set, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(flat);
        FolderFile[] files = [.. flat.Entries.Select(e => new FolderFile(e.Key, e.Value.Mode, e.Value.Hash, e.Value.Size))];
        if (!FolderTrees.TryBuild(files, out IReadOnlyList<Tree> trees, out problem))
        {
            set = null;
            return false;
        }

        set = new TreeSet(trees);
        return true;
    }

    /// <summary>The empty Tree: the root of an empty folder, and the merge base of two leaves without a common ancestor.</summary>
    public static Tree EmptyTree { get; } = ObjectBuilders.TryTree([], out Tree? empty, out _) ? empty : throw new InvalidOperationException("The empty Tree cannot be built.");

    /// <inheritdoc />
    public override string ToString() => $"{Trees.Count} Trees, root {Root}";
}
