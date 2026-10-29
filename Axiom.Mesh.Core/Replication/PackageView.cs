using System.Collections.Generic;

namespace Axiom.Mesh.Core;

/// <summary>
/// What this node holds of one Package, as the planner compares it with a peer's <c>PackageState</c> (SPEC.md §10.2):
/// where it stands, its current Manifest and owner, the current Grants' hashes and the stored Heads.
/// </summary>
/// <param name="Package">The Package.</param>
/// <param name="State">Not known, listed or followed.</param>
/// <param name="Manifest">The current Manifest's seq and hash, or <see langword="null"/> while none is held.</param>
/// <param name="Owner">The owner, from the current Manifest; zero without one.</param>
/// <param name="Grants">The current Grants' hashes.</param>
/// <param name="Heads">The stored Heads, ascending by device id.</param>
internal sealed record PackageView(PackageId Package, PackageState State, ObjectSeq? Manifest, UserId Owner, IReadOnlySet<ObjectHash> Grants, IReadOnlyList<Head> Heads)
{
    /// <summary>The view of a Package as <paramref name="trust"/> and <paramref name="heads"/> hold it.</summary>
    /// <param name="package">The Package.</param>
    /// <param name="trust">The node's trust now.</param>
    /// <param name="heads">The stored Heads now.</param>
    /// <returns>The view; state <see cref="PackageState.None"/> for a Package the node does not know.</returns>
    public static PackageView Of(PackageId package, TrustSnapshot trust, HeadsView heads)
    {
        if (!trust.TryGetPackage(package, out PackageTrust? held))
            return new PackageView(package, PackageState.None, null, default, new HashSet<ObjectHash>(), []);

        var grants = new HashSet<ObjectHash>();
        foreach (VerifiedGrant grant in held.Grants.Values)
            grants.Add(grant.Hash);
        return new PackageView(package, held.State, held.Manifest?.Content.Stamp, held.Manifest?.Content.Owner ?? default, grants, heads.Of(package));
    }
}
