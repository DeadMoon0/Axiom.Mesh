using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Axiom.Mesh.Core;

/// <summary>
/// The writer's working copy of trust: started from a <see cref="TrustSnapshot"/>, changed by the objects a batch
/// stores, and frozen into the next snapshot when the batch commits. Applies the rules of SPEC.md §8.7–§8.9 through
/// Model (<see cref="CurrentObject"/>, <see cref="RosterRules"/>). Owned by the single writer; not thread-safe.
/// </summary>
internal sealed class TrustBuilder
{
    private readonly Dictionary<UserId, UserTrust> users;
    private readonly Dictionary<PackageId, PackageTrust> packages;
    private readonly Dictionary<DeviceId, PublicKey> keys;
    private SelfIdentity? self;
    private long version;

    /// <summary>A working copy of <paramref name="from"/>.</summary>
    /// <param name="from">The snapshot to start from.</param>
    /// <param name="keys">The device keys handed out so far, reused so that each device keeps one key instance.</param>
    public TrustBuilder(TrustSnapshot from, Dictionary<DeviceId, PublicKey> keys)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(keys);
        users = from.Users.ToDictionary(u => u.Id);
        packages = from.Packages.ToDictionary(p => p.Id);
        this.keys = keys;
        self = from.Self;
        version = from.Version;
    }

    /// <summary>The own identity, or <see langword="null"/>.</summary>
    public SelfIdentity? Self => self;

    /// <summary>The Packages held.</summary>
    public IEnumerable<PackageTrust> Packages => packages.Values;

    /// <summary>Freezes the working copy into the next snapshot.</summary>
    /// <returns>The snapshot, with a higher version than the one this copy started from.</returns>
    public TrustSnapshot Build() => new(++version, self, users.Values, packages.Values, keys);

    /// <summary>Sets the own identity; the own user is known from then on.</summary>
    /// <param name="identity">The identity.</param>
    public void SetSelf(SelfIdentity identity)
    {
        self = identity;
        Pair(identity.UserKey);
    }

    /// <summary>Records a user as known through pairing (SPEC.md §1.2, §13).</summary>
    /// <param name="userKey">The user's key.</param>
    /// <returns><see langword="true"/> when that changed anything.</returns>
    public bool Pair(PublicKey userKey)
    {
        ArgumentNullException.ThrowIfNull(userKey);
        UserId id = userKey.AsUserId();
        if (users.TryGetValue(id, out UserTrust? user) && user.Paired)
            return false;
        users[id] = user is null
            ? new UserTrust(userKey, paired: true, roster: null, [], new HashSet<DeviceId>())
            : new UserTrust(user.Key, paired: true, user.Roster, user.Devices, user.RemovedEver);
        return true;
    }

    /// <summary>Whether the node trusts the user's id now (SPEC.md §1.2).</summary>
    /// <param name="user">The user.</param>
    /// <returns><see langword="true"/> when paired, own, or owner or grantee of a followed Package with its Manifest.</returns>
    public bool IsKnown(UserId user) =>
        (users.TryGetValue(user, out UserTrust? held) && held.Paired)
        || packages.Values.Any(p => p.IsFollowed && p.Manifest is { } m && (m.Content.Owner == user || p.Grants.ContainsKey(user)));

    /// <summary>The user, when held.</summary>
    /// <param name="user">The user id.</param>
    /// <param name="trust">The user, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when held.</returns>
    public bool TryGetUser(UserId user, [NotNullWhen(true)] out UserTrust? trust) => users.TryGetValue(user, out trust);

    /// <summary>The Package, when held.</summary>
    /// <param name="package">The Package id.</param>
    /// <param name="trust">The Package, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when held.</returns>
    public bool TryGetPackage(PackageId package, [NotNullWhen(true)] out PackageTrust? trust) => packages.TryGetValue(package, out trust);

    /// <summary>
    /// Makes <paramref name="next"/> the user's current Roster when it supersedes the one held (SPEC.md §8.8), applying
    /// permanent removal and the one-user-per-device rule.
    /// </summary>
    /// <param name="next">The verified Roster of a known user.</param>
    /// <param name="previous">The Roster it replaced, or <see langword="null"/>.</param>
    /// <returns>What applying it decided, or <see langword="null"/> when it does not supersede the current one.</returns>
    public RosterApplication? ApplyRoster(VerifiedRoster next, out VerifiedRoster? previous)
    {
        ArgumentNullException.ThrowIfNull(next);
        UserId id = next.Content.User;
        users.TryGetValue(id, out UserTrust? user);
        previous = user?.Roster;
        if (previous is not null && !CurrentObject.Supersedes(next.Content.Stamp, previous.Content.Stamp))
            return null;

        var heldByOthers = new HashSet<DeviceId>(users.Values.Where(u => u.Id != id).SelectMany(u => u.Devices).Select(d => d.Device.Id));
        RosterApplication application = RosterRules.Apply(previous, next, user?.RemovedEver ?? new HashSet<DeviceId>(), heldByOthers);
        foreach (EffectiveDevice device in application.Devices)
            keys.TryAdd(device.Device.Id, device.Device.Key);

        users[id] = new UserTrust(user?.Key ?? next.Content.UserKey, user?.Paired ?? false, next, application.Devices, application.RemovedEver.ToHashSet());
        return application;
    }

    /// <summary>Restores a user as the database holds it, at start.</summary>
    /// <param name="user">The user.</param>
    public void Restore(UserTrust user)
    {
        ArgumentNullException.ThrowIfNull(user);
        users[user.Id] = user;
        foreach (EffectiveDevice device in user.Devices)
            keys.TryAdd(device.Device.Id, device.Device.Key);
    }

    /// <summary>Restores or replaces a Package.</summary>
    /// <param name="package">The Package.</param>
    public void SetPackage(PackageTrust package)
    {
        ArgumentNullException.ThrowIfNull(package);
        packages[package.Id] = package;
    }

    /// <summary>Sets a Package's state, keeping everything else.</summary>
    /// <param name="package">The Package id.</param>
    /// <param name="state">The new state.</param>
    /// <returns>The Package as it is now.</returns>
    public PackageTrust SetState(PackageId package, PackageState state) =>
        Replace(package, p => new PackageTrust(package, state, p?.Manifest, p?.Grants ?? new Dictionary<UserId, VerifiedGrant>(), p?.Policy ?? LocalRetention.Default, p?.WithdrawnGrant));

    /// <summary>Sets a Package's local retention policy.</summary>
    /// <param name="package">The Package id.</param>
    /// <param name="policy">The policy.</param>
    /// <returns>The Package as it is now.</returns>
    public PackageTrust SetPolicy(PackageId package, LocalRetention policy) =>
        Replace(package, p => new PackageTrust(package, p?.State ?? PackageState.None, p?.Manifest, p?.Grants ?? new Dictionary<UserId, VerifiedGrant>(), policy, p?.WithdrawnGrant));

    /// <summary>Makes a Manifest current for its Package.</summary>
    /// <param name="manifest">The Manifest, which supersedes the current one.</param>
    /// <returns>The Package as it is now.</returns>
    public PackageTrust SetManifest(VerifiedManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        PackageId id = manifest.Content.Package;
        return Replace(id, p => new PackageTrust(id, p?.State ?? PackageState.None, manifest, p?.Grants ?? new Dictionary<UserId, VerifiedGrant>(), p?.Policy ?? LocalRetention.Default, p?.WithdrawnGrant));
    }

    /// <summary>Makes a Grant current for its Package and grantee.</summary>
    /// <param name="grant">The Grant, which supersedes the current one.</param>
    /// <returns>The Package as it is now.</returns>
    public PackageTrust SetGrant(VerifiedGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        PackageId id = grant.Content.Package;
        return Replace(id, p =>
        {
            var grants = new Dictionary<UserId, VerifiedGrant>(p?.Grants ?? new Dictionary<UserId, VerifiedGrant>())
            {
                [grant.Content.Grantee] = grant,
            };
            return new PackageTrust(id, p?.State ?? PackageState.None, p?.Manifest, grants, p?.Policy ?? LocalRetention.Default, p?.WithdrawnGrant);
        });
    }

    /// <summary>
    /// Treats the own user's current Grant of a Package as withdrawn (SPEC.md §7.2): an owner's device listed the Package
    /// before and now omits it. A newer Grant, with another hash, counts again.
    /// </summary>
    /// <param name="package">The Package.</param>
    /// <returns><see langword="true"/> when a current own Grant was withdrawn now.</returns>
    public bool WithdrawOwnGrant(PackageId package)
    {
        if (self is null || !packages.TryGetValue(package, out PackageTrust? p) || !p.Grants.TryGetValue(self.User, out VerifiedGrant? own) || p.WithdrawnGrant == own.Hash)
            return false;
        packages[package] = new PackageTrust(package, p.State, p.Manifest, p.Grants, p.Policy, own.Hash);
        return true;
    }

    /// <summary>Whether a device stands, not removed or removed, in the current Roster of the Package's owner (SPEC.md §9.4).</summary>
    /// <param name="package">The Package.</param>
    /// <param name="device">The device.</param>
    /// <returns><see langword="true"/> when it is an owner device.</returns>
    public bool IsOwnerDevice(PackageId package, DeviceId device) =>
        packages.TryGetValue(package, out PackageTrust? p) && p.Manifest is { } manifest
        && users.TryGetValue(manifest.Content.Owner, out UserTrust? owner) && owner.Devices.Any(d => d.Device.Id == device);

    private PackageTrust Replace(PackageId id, Func<PackageTrust?, PackageTrust> change)
    {
        packages.TryGetValue(id, out PackageTrust? current);
        PackageTrust next = change(current);
        packages[id] = next;
        return next;
    }
}
