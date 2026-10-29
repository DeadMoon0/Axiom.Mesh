using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Axiom.Mesh.Core;

/// <summary>
/// Everything the node trusts at one moment, immutable (plan decision D8): the known users with their current Rosters
/// and the effective device set (SPEC.md §8.8), the Packages with their state, current Manifest and Grants (§8.7,
/// §8.9), and the own identity. The single writer builds a new one when trust changes and swaps it in whole; TLS
/// callbacks, sessions, intake and the local port read it without a lock.
/// </summary>
/// <remarks>
/// A Package's Manifest and Grants count for <see cref="ITrustSnapshotView"/> only while it is followed: a listed
/// Package's Manifest makes nobody known and admits nothing (SPEC.md §10.6). One <see cref="PublicKey"/> instance
/// stands for each device across snapshots, so its verifier is made once.
/// </remarks>
internal sealed class TrustSnapshot : ITrustSnapshotView
{
    private readonly FrozenDictionary<UserId, UserTrust> users;
    private readonly FrozenDictionary<PackageId, PackageTrust> packages;
    private readonly FrozenDictionary<DeviceId, TrustedDevice> devices;
    private readonly FrozenSet<UserId> known;

    /// <summary>Freezes what the writer holds.</summary>
    /// <param name="version">A number that grows with every snapshot the writer swaps in.</param>
    /// <param name="self">The own identity, or <see langword="null"/> before one exists.</param>
    /// <param name="users">The users the node holds.</param>
    /// <param name="packages">The Packages the node holds.</param>
    /// <param name="keys">One key instance per device id, reused across snapshots.</param>
    public TrustSnapshot(long version, SelfIdentity? self, IEnumerable<UserTrust> users, IEnumerable<PackageTrust> packages, IReadOnlyDictionary<DeviceId, PublicKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        Version = version;
        Self = self;
        this.users = users.ToFrozenDictionary(u => u.Id);
        this.packages = packages.ToFrozenDictionary(p => p.Id);
        known = KnownUsers(this.users.Values, this.packages.Values).ToFrozenSet();
        devices = Devices(this.users.Values.Where(u => known.Contains(u.Id)), keys);
    }

    /// <summary>The trust of a node that holds nothing yet.</summary>
    public static TrustSnapshot Empty { get; } = new(0, null, [], [], new Dictionary<DeviceId, PublicKey>());

    /// <summary>Grows with every snapshot the writer swaps in.</summary>
    public long Version { get; }

    /// <summary>The own identity, or <see langword="null"/> before one exists.</summary>
    public SelfIdentity? Self { get; }

    /// <summary>The users the node holds, known or not.</summary>
    public IEnumerable<UserTrust> Users => users.Values;

    /// <summary>The Packages the node holds, followed or listed.</summary>
    public IEnumerable<PackageTrust> Packages => packages.Values;

    /// <summary>Whether the node trusts <paramref name="user"/>'s id (SPEC.md §1.2): paired, own, or owner or grantee of a followed Package.</summary>
    /// <param name="user">The user.</param>
    /// <returns><see langword="true"/> when the user is known.</returns>
    public bool IsKnownUser(UserId user) => known.Contains(user);

    /// <summary>The user, when the node holds one.</summary>
    /// <param name="user">The user id.</param>
    /// <param name="trust">The user, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when held.</returns>
    public bool TryGetUser(UserId user, [NotNullWhen(true)] out UserTrust? trust) => users.TryGetValue(user, out trust);

    /// <summary>The Package, followed or listed, when the node holds it.</summary>
    /// <param name="package">The Package id.</param>
    /// <param name="trust">The Package, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when held.</returns>
    public bool TryGetPackage(PackageId package, [NotNullWhen(true)] out PackageTrust? trust) => packages.TryGetValue(package, out trust);

    /// <summary>Whether the node follows the Package.</summary>
    /// <param name="package">The Package id.</param>
    /// <returns><see langword="true"/> when followed.</returns>
    public bool IsFollowed(PackageId package) => packages.TryGetValue(package, out PackageTrust? p) && p.IsFollowed;

    /// <summary>Whether the node follows the Package or a peer lists it.</summary>
    /// <param name="package">The Package id.</param>
    /// <returns><see langword="true"/> when followed or listed.</returns>
    public bool IsFollowedOrListed(PackageId package) => packages.TryGetValue(package, out PackageTrust? p) && p.State != PackageState.None;

    /// <summary>Whether a device of a known user's current Roster is removed (SPEC.md §8.8); an unknown device is not "removed".</summary>
    /// <param name="device">The device id.</param>
    /// <returns><see langword="true"/> when its Roster removes it.</returns>
    public bool IsRemoved(DeviceId device) => devices.TryGetValue(device, out TrustedDevice d) && d.IsRemoved;

    /// <summary>
    /// The device a TLS peer presents, by its 65-byte public key (SPEC.md §2.3, §4.3): pinned by <c>H(point)</c> to a
    /// not-removed device of a known user's current Roster. The TLS callback calls this with the certificate's raw
    /// point, so nothing is parsed for a key no Roster lists.
    /// </summary>
    /// <param name="point">The uncompressed P-256 point, <c>0x04 ‖ X ‖ Y</c>.</param>
    /// <param name="device">The device id, or zero.</param>
    /// <param name="user">The device's user, or zero.</param>
    /// <returns><see langword="true"/> when the key belongs to a not-removed device the node trusts.</returns>
    public bool TryResolveDevice(ReadOnlySpan<byte> point, out DeviceId device, out UserId user)
    {
        device = default;
        user = default;
        if (point.Length != PublicKey.Length)
            return false;

        var id = new DeviceId(Hash32.Of(point));
        if (!devices.TryGetValue(id, out TrustedDevice found) || found.IsRemoved || !found.Key.Encoded.SequenceEqual(point))
            return false;

        device = id;
        user = found.User;
        return true;
    }

    /// <summary>Whether a Package may be listed, claimed or served to a peer's user (SPEC.md §7.2, §9.7); only a followed Package with its Manifest is.</summary>
    /// <param name="peerUser">The peer's user.</param>
    /// <param name="package">The Package id.</param>
    /// <returns><see langword="true"/> when the peer may see it.</returns>
    public bool MayList(UserId peerUser, PackageId package) =>
        packages.TryGetValue(package, out PackageTrust? p) && p.IsFollowed && p.Audience is { } audience && Visibility.MayList(peerUser, audience);

    /// <summary>Whether a Grant may be listed, claimed or sent to a peer's user (SPEC.md §9.7): to the owner every one, to a grantee its own.</summary>
    /// <param name="peerUser">The peer's user.</param>
    /// <param name="grant">The Grant.</param>
    /// <returns><see langword="true"/> when the peer may see it.</returns>
    public bool MayServeGrant(UserId peerUser, VerifiedGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return packages.TryGetValue(grant.Content.Package, out PackageTrust? p) && p.IsFollowed && p.Manifest is { } manifest
            && Visibility.MayClaimGrant(peerUser, manifest.Content.Owner, grant.Content.Grantee);
    }

    /// <summary>Whether a user's Roster may be served to a peer's user (SPEC.md §9.8).</summary>
    /// <param name="rosterUser">Whose Roster.</param>
    /// <param name="peerUser">The peer's user.</param>
    /// <returns><see langword="true"/> when it may be served.</returns>
    public bool MayServeRoster(UserId rosterUser, UserId peerUser) =>
        Self is { } self
        && Visibility.MayServeRoster(rosterUser, peerUser, self.User, packages.Values.Where(p => p.IsFollowed && p.Audience is not null).Select(p => p.Audience!));

    /// <inheritdoc />
    public bool TryGetManifest(PackageId package, [NotNullWhen(true)] out VerifiedManifest? manifest)
    {
        manifest = packages.TryGetValue(package, out PackageTrust? p) && p.IsFollowed ? p.Manifest : null;
        return manifest is not null;
    }

    /// <inheritdoc />
    public bool TryGetGrant(PackageId package, UserId grantee, [NotNullWhen(true)] out VerifiedGrant? grant)
    {
        grant = null;
        return packages.TryGetValue(package, out PackageTrust? p) && p.IsFollowed && p.Grants.TryGetValue(grantee, out grant);
    }

    /// <inheritdoc />
    public bool TryGetDevice(DeviceId device, out TrustedDevice trusted) => devices.TryGetValue(device, out trusted);

    private static IEnumerable<UserId> KnownUsers(IEnumerable<UserTrust> users, IEnumerable<PackageTrust> packages)
    {
        foreach (UserTrust user in users.Where(u => u.Paired))
            yield return user.Id;

        foreach (PackageTrust package in packages.Where(p => p.IsFollowed && p.Manifest is not null))
        {
            yield return package.Manifest!.Content.Owner;
            foreach (UserId grantee in package.Grants.Keys)
                yield return grantee;
        }
    }

    // A device id stands in at most one current Roster the node acts on; where two users list it, RosterRules removed it
    // in the later one, so the listing that keeps it is the one that counts.
    private static FrozenDictionary<DeviceId, TrustedDevice> Devices(IEnumerable<UserTrust> knownUsers, IReadOnlyDictionary<DeviceId, PublicKey> keys)
    {
        var devices = new Dictionary<DeviceId, TrustedDevice>();
        foreach (UserTrust user in knownUsers)
        {
            foreach (EffectiveDevice listed in user.Devices)
            {
                DeviceId id = listed.Device.Id;
                PublicKey key = keys.TryGetValue(id, out PublicKey? cached) ? cached : listed.Device.Key;
                var trusted = new TrustedDevice(id, user.Id, key, listed.IsRemoved);
                if (!devices.TryGetValue(id, out TrustedDevice existing) || (existing.IsRemoved && !trusted.IsRemoved))
                    devices[id] = trusted;
            }
        }

        return devices.ToFrozenDictionary();
    }
}
