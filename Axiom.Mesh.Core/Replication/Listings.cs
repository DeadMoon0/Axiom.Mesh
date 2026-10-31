using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Protocol;
using WireState = Axiom.Mesh.Protocol.PackageState;

namespace Axiom.Mesh.Core;

/// <summary>
/// What this node lists to a peer (SPEC.md §7.2, §7.3, §9.7), as pure functions of its trust and Heads: exactly the
/// followed Packages whose owner is the peer's user or that grant it <c>Read</c> or <c>Write</c>; to the owner's devices
/// every current Grant, to anyone else only the Grant naming them; the Heads ascending by device. Pages hold 64
/// Packages, which always fit a frame (a full <c>PackageState</c> is under 27 KiB).
/// </summary>
internal static class Listings
{
    /// <summary>The Packages a peer's user may see, which a listing holds exactly.</summary>
    /// <param name="peer">The peer's user.</param>
    /// <param name="trust">The node's trust now.</param>
    /// <returns>The Package ids.</returns>
    public static HashSet<PackageId> Listable(UserId peer, TrustSnapshot trust)
    {
        ArgumentNullException.ThrowIfNull(trust);
        var listable = new HashSet<PackageId>();
        foreach (PackageTrust package in trust.Packages)
        {
            if (trust.MayList(peer, package.Id))
                listable.Add(package.Id);
        }

        return listable;
    }

    /// <summary>The state of a Package as listed to a peer who may see it.</summary>
    /// <param name="package">The Package; followed, with a Manifest.</param>
    /// <param name="peer">The peer's user.</param>
    /// <param name="heads">The stored Heads.</param>
    /// <returns>The state.</returns>
    public static WireState StateFor(PackageTrust package, UserId peer, HeadsView heads)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(heads);
        VerifiedManifest manifest = package.Manifest ?? throw new ArgumentException("A Package without a Manifest is never listed (SPEC.md §7.2).", nameof(package));
        IEnumerable<VerifiedGrant> grants = peer == manifest.Content.Owner
            ? package.Grants.Values
            : package.Grants.TryGetValue(peer, out VerifiedGrant? own) ? [own] : [];
        IReadOnlyList<Head> held = heads.Of(package.Id);
        return new WireState(
            package.Id,
            manifest.Hash,
            manifest.Content.Seq,
            [.. grants.Select(g => g.Hash).Order().Take(Limits.MaxPackageGrants)],
            held.Count <= Limits.MaxPackageHeads ? held : [.. held.OrderByDescending(h => h.Clock).ThenBy(h => h.Device).Take(Limits.MaxPackageHeads).OrderBy(h => h.Device)]);
    }

    /// <summary>
    /// The state that carries a revocation to the revoked user (SPEC.md §9.7, Appendix B step 7): the Package's Manifest
    /// and the Grant naming the peer, whatever its rights, and no Heads.
    /// </summary>
    /// <param name="package">The Package; followed, with a Manifest and a Grant to the peer.</param>
    /// <param name="grant">The Grant naming the peer's user.</param>
    /// <returns>The state.</returns>
    public static WireState RevocationFor(PackageTrust package, VerifiedGrant grant)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(grant);
        VerifiedManifest manifest = package.Manifest ?? throw new ArgumentException("A Grant is held only with its Manifest (SPEC.md §8.9).", nameof(package));
        return new WireState(package.Id, manifest.Hash, manifest.Content.Seq, [grant.Hash], []);
    }

    /// <summary>A complete listing to a peer, cut into pages of 64; an empty listing is one empty last page.</summary>
    /// <param name="peer">The peer's user.</param>
    /// <param name="listable">The Packages it may see (<see cref="Listable"/>).</param>
    /// <param name="trust">The node's trust now.</param>
    /// <param name="heads">The stored Heads.</param>
    /// <returns>The pages, in order.</returns>
    public static IReadOnlyList<MeshMessage> Pages(UserId peer, IReadOnlyCollection<PackageId> listable, TrustSnapshot trust, HeadsView heads)
    {
        ArgumentNullException.ThrowIfNull(listable);
        ArgumentNullException.ThrowIfNull(trust);
        WireState[] states = [.. listable
            .Order()
            .Take(Limits.MaxListingPackages)
            .Select(id => trust.TryGetPackage(id, out PackageTrust? package) ? StateFor(package, peer, heads) : null)
            .OfType<WireState>()];
        if (states.Length == 0)
            return [new Packages(0, true, [])];

        var pages = new List<MeshMessage>((states.Length + Limits.MaxPackagesPerPage - 1) / Limits.MaxPackagesPerPage);
        for (int at = 0; at < states.Length; at += Limits.MaxPackagesPerPage)
        {
            int count = Math.Min(Limits.MaxPackagesPerPage, states.Length - at);
            pages.Add(new Packages((ushort)pages.Count, at + count == states.Length, states[at..(at + count)]));
        }

        return pages;
    }
}
