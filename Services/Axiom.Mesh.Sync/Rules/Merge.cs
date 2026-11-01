using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The merge of two leaves of a branch (SPEC.md §9.5), path by path over the union of both Trees against the common
/// ancestor's Tree:
/// <list type="bullet">
/// <item>changed on neither side: the ancestor's entry;</item>
/// <item>changed on one side only, a deletion included: that side's state;</item>
/// <item>changed on both sides identically: that entry;</item>
/// <item>changed on both sides differently: the entry of the side whose leaf is later in the total order, and the other side's
/// file kept beside it under <see cref="ConflictName"/>.</item>
/// </list>
/// A directory changed differently on both sides is merged path by path within it (a side without it counts as an empty
/// directory there, and a directory the merge leaves empty that one side deleted is gone). A directory replaced by a file, or
/// the reverse, counts as changed differently: the later side's entry stands and the other side's file or whole subtree is kept
/// beside it whatever its mode (the stricter reading of SPEC.md §9.5 recorded in CONFORMANCE.md: "the other side's file"
/// includes a subtree, so nothing is lost). No content is merged. The result does not depend on which leaf is called ours:
/// every node that merges the same two leaves makes the same Tree. Pure.
/// </summary>
internal static class Merge
{
    /// <summary>Merges two leaves.</summary>
    /// <param name="ancestor">The common ancestor's Tree; <see cref="TreeSet.EmptyTree"/> when there is none.</param>
    /// <param name="ours">One leaf's Tree.</param>
    /// <param name="theirs">The other leaf's Tree.</param>
    /// <param name="ourKey">Our leaf's place in the total order (a virtual leaf's for local changes not yet written).</param>
    /// <param name="theirKey">Their leaf's place in the total order.</param>
    /// <param name="trees">Every Tree below the three, by hash.</param>
    /// <param name="merged">The merge, or <see langword="null"/>.</param>
    /// <param name="problem">Why there is none: a Tree not at hand, or a directory the merge would make larger than a Tree may be.</param>
    /// <returns><see langword="true"/> when merged.</returns>
    public static bool TryOf(Tree ancestor, Tree ours, Tree theirs, EntryKey ourKey, EntryKey theirKey, IReadOnlyDictionary<ObjectHash, Tree> trees, out MergedTree? merged, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        ArgumentNullException.ThrowIfNull(ours);
        ArgumentNullException.ThrowIfNull(theirs);
        ArgumentNullException.ThrowIfNull(trees);
        bool oursLater = ourKey > theirKey;
        (Tree later, Tree earlier, DeviceId loser) = oursLater ? (ours, theirs, theirKey.Device) : (theirs, ours, ourKey.Device);
        var run = new Run(trees, loser);
        Tree? root = run.Directory(string.Empty, ancestor, later, earlier);
        if (root is null)
        {
            (merged, problem) = (null, run.Problem);
            return false;
        }

        merged = new MergedTree(root.Hash, run.Made, [.. run.Copies.OrderBy(c => c.Path, StringComparer.Ordinal)], root.Hash == later.Hash);
        problem = null;
        return true;
    }

    // One merge: the Trees it made and the copies it kept, or the first problem.
    private sealed class Run(IReadOnlyDictionary<ObjectHash, Tree> trees, DeviceId loser)
    {
        public List<Tree> Made { get; } = [];

        public List<ConflictCopy> Copies { get; } = [];

        public string? Problem { get; private set; }

        // The merged Tree of one directory; a missing side counts as empty.
        public Tree? Directory(string prefix, Tree? ancestor, Tree? later, Tree? earlier)
        {
            Dictionary<string, TreeEntry> a = EntriesOf(ancestor), w = EntriesOf(later), l = EntriesOf(earlier);
            var result = new List<TreeEntry>();
            var losers = new List<(string Name, TreeEntry Entry)>();
            foreach (string name in a.Keys.Concat(w.Keys).Concat(l.Keys).Distinct().Order(StringComparer.Ordinal))
            {
                TreeEntry? ea = Find(a, name), ew = Find(w, name), el = Find(l, name);
                if (Same(ew, el) || Same(el, ea))
                {
                    Keep(result, ew);
                }
                else if (Same(ew, ea))
                {
                    Keep(result, el);
                }
                else if (IsDirectoryOrAbsent(ew) && IsDirectoryOrAbsent(el))
                {
                    // Both changed the directory, differently: merge inside it, path by path.
                    Tree? merged = Directory(prefix + name + "/", TreeOf(ea), TreeOf(ew), TreeOf(el));
                    if (merged is null)
                        return null;
                    if (merged.Entries.Count > 0 || (ew is not null && el is not null))
                        result.Add(new TreeEntry(name, TreeEntryMode.Directory, merged.Hash, 0));
                }
                else
                {
                    // Changed differently, a file on at least one side: the later side stands, the other side's file or subtree lies beside.
                    Keep(result, ew);
                    if (el is { } copy)
                        losers.Add((name, copy));
                }

                if (Problem is not null)
                    return null;
            }

            // Copy names last, against every name the directory holds, so the choice depends on the directory only.
            var taken = new HashSet<string>(result.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
            foreach ((string name, TreeEntry entry) in losers)
            {
                string copyName = ConflictName.Of(name, loser, taken);
                taken.Add(copyName);
                result.Add(entry with { Name = copyName });
                Copies.Add(new ConflictCopy(prefix + name, prefix + copyName, loser));
            }

            if (later is not null && result.Count == later.Entries.Count && result.Zip(later.Entries.OrderBy(e => e.Name, StringComparer.Ordinal)).All(p => p.First == p.Second))
                return later;
            if (!ObjectBuilders.TryTree(result, out Tree? tree, out string? problem))
            {
                Problem = $"{(prefix.Length == 0 ? "the root" : prefix.TrimEnd('/'))}: {problem}";
                return null;
            }

            if (tree.Hash != later?.Hash && tree.Hash != earlier?.Hash && tree.Hash != ancestor?.Hash)
                Made.Add(tree);
            return tree;
        }

        private Tree? TreeOf(TreeEntry? entry)
        {
            if (entry is not { Mode: TreeEntryMode.Directory } directory)
                return null;
            if (trees.TryGetValue(directory.Hash, out Tree? tree))
                return tree;
            Problem ??= $"the Tree {directory.Hash} of {directory.Name} is not at hand";
            return null;
        }

        private Dictionary<string, TreeEntry> EntriesOf(Tree? tree) =>
            tree is null ? [] : tree.Entries.ToDictionary(e => e.Name, StringComparer.Ordinal);

        private static TreeEntry? Find(Dictionary<string, TreeEntry> entries, string name) => entries.TryGetValue(name, out TreeEntry entry) ? entry : null;

        private static bool Same(TreeEntry? x, TreeEntry? y) =>
            x is null ? y is null : y is { } other && x.Value.Mode == other.Mode && x.Value.Hash == other.Hash && x.Value.Size == other.Size;

        private static bool IsDirectoryOrAbsent(TreeEntry? entry) => entry is null or { Mode: TreeEntryMode.Directory };

        private static void Keep(List<TreeEntry> result, TreeEntry? entry)
        {
            if (entry is { } present)
                result.Add(present);
        }
    }
}
