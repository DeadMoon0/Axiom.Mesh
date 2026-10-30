using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Core;

/// <summary>
/// Which Packages a <c>ListPackages</c> page walks (LOCAL-SPEC.md §6.4), as a function of trust: every Package the Host
/// knows, Listed and Followed, or Followed only; of the given kinds and owner; after the cursor; ascending by id.
/// </summary>
internal static class PackageListing
{
    /// <summary>The Packages, in order.</summary>
    /// <param name="trust">The node's trust.</param>
    /// <param name="after">The cursor: the last Package of the previous page; <see langword="null"/> from the start.</param>
    /// <param name="kinds">The Package kinds; empty for every kind.</param>
    /// <param name="owner">Only Packages of this owner; <see langword="null"/> for any.</param>
    /// <param name="followedOnly">Only followed Packages.</param>
    /// <returns>The Package ids, ascending.</returns>
    public static IEnumerable<PackageId> Select(TrustSnapshot trust, PackageId? after, IReadOnlyList<PackageKind> kinds, UserId? owner, bool followedOnly) =>
        trust.Packages
            .Where(p => p.State != PackageState.None)
            .Where(p => !followedOnly || p.IsFollowed)
            .Where(p => after is not { } cursor || p.Id > cursor)
            .Where(p => kinds.Count == 0 || (p.Manifest is { } m && kinds.Contains(m.Content.Kind)))
            .Where(p => owner is not { } o || (p.Manifest is { } m2 && m2.Content.Owner == o))
            .Select(p => p.Id)
            .Order();
}
