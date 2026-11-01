using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The leaves of a branch (SPEC.md §9.5): the stored Versions with that label from which no other stored Version with that
/// label descends, through any parents of any label; and the head for display, the leaf highest in the total order. Pure.
/// </summary>
internal static class Leaves
{
    /// <summary>The leaves of a branch, ascending in the total order.</summary>
    /// <param name="versions">Every stored Version of the Archive, by hash; parents not in it are not stored here.</param>
    /// <param name="label">The branch.</param>
    /// <returns>The leaves; empty when the branch has no Version.</returns>
    public static IReadOnlyList<VersionFact> Of(IReadOnlyDictionary<ObjectHash, VersionFact> versions, string label)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(label);
        var below = new HashSet<ObjectHash>();
        var pending = new Stack<ObjectHash>();
        foreach (VersionFact version in versions.Values)
        {
            if (version.Label != label)
                continue;
            foreach (ObjectHash parent in version.Parents)
                pending.Push(parent);
        }

        // Everything reachable through at least one parent step from a Version of the branch has a descendant on it.
        while (pending.TryPop(out ObjectHash hash))
        {
            if (!below.Add(hash) || !versions.TryGetValue(hash, out VersionFact? reached))
                continue;
            foreach (ObjectHash parent in reached.Parents)
                pending.Push(parent);
        }

        return [.. versions.Values.Where(v => v.Label == label && !below.Contains(v.Hash)).OrderBy(v => v.Key)];
    }

    /// <summary>The head of a branch for display: the leaf highest in the total order.</summary>
    /// <param name="leaves">The leaves.</param>
    /// <returns>The head, or <see langword="null"/> when there is none.</returns>
    public static VersionFact? Head(IReadOnlyList<VersionFact> leaves)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        return leaves.Count == 0 ? null : leaves.MaxBy(l => l.Key);
    }
}
