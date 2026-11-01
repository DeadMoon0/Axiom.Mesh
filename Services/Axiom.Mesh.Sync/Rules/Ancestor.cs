using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The common ancestor of two leaves (SPEC.md §9.5): the Version highest in the total order among those reachable from both,
/// each counting as reachable from itself; none when they share no history (the merge then runs against the empty tree).
/// With several unrelated common ancestors (a criss-cross) the highest one is taken, as the spec says; nothing is merged
/// recursively. Pure.
/// </summary>
internal static class Ancestor
{
    /// <summary>The common ancestor of two Versions.</summary>
    /// <param name="versions">Every stored Version of the Archive, by hash.</param>
    /// <param name="a">One leaf.</param>
    /// <param name="b">The other.</param>
    /// <returns>The ancestor, or <see langword="null"/> for none.</returns>
    public static VersionFact? Of(IReadOnlyDictionary<ObjectHash, VersionFact> versions, VersionFact a, VersionFact b)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        HashSet<ObjectHash> fromA = Reachable(versions, a);
        return Reachable(versions, b).Where(fromA.Contains).Select(h => versions.TryGetValue(h, out VersionFact? v) ? v : null)
            .OfType<VersionFact>()
            .MaxBy(v => v.Key);
    }

    private static HashSet<ObjectHash> Reachable(IReadOnlyDictionary<ObjectHash, VersionFact> versions, VersionFact from)
    {
        var seen = new HashSet<ObjectHash>();
        var pending = new Stack<ObjectHash>([from.Hash]);
        while (pending.TryPop(out ObjectHash hash))
        {
            if (!versions.TryGetValue(hash, out VersionFact? version) || !seen.Add(hash))
                continue;
            foreach (ObjectHash parent in version.Parents)
                pending.Push(parent);
        }

        return seen;
    }
}
