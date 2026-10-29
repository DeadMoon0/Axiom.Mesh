using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Axiom.Mesh.Tests.Support;

/// <summary>A node's trust as dictionaries, built up by a test.</summary>
internal sealed class TrustView : ITrustSnapshotView
{
    private readonly Dictionary<PackageId, VerifiedManifest> manifests = [];
    private readonly Dictionary<(PackageId, UserId), VerifiedGrant> grants = [];
    private readonly Dictionary<DeviceId, TrustedDevice> devices = [];
    private readonly Dictionary<UserId, (VerifiedRoster Roster, IReadOnlyCollection<DeviceId> RemovedEver)> rosters = [];

    public TrustView With(VerifiedManifest manifest)
    {
        manifests[manifest.Content.Package] = manifest;
        return this;
    }

    public TrustView With(VerifiedGrant grant)
    {
        grants[(grant.Content.Package, grant.Content.Grantee)] = grant;
        return this;
    }

    /// <summary>Applies a Roster as the user's new current one, by the rules of SPEC.md §8.8.</summary>
    public TrustView With(VerifiedRoster roster)
    {
        UserId user = roster.Content.User;
        rosters.TryGetValue(user, out var previous);
        var others = new HashSet<DeviceId>();
        foreach (TrustedDevice device in devices.Values)
        {
            if (device.User != user)
                others.Add(device.Id);
        }

        RosterApplication applied = RosterRules.Apply(previous.Roster, roster, new HashSet<DeviceId>(previous.RemovedEver ?? []), others);
        foreach (EffectiveDevice device in applied.Devices)
            devices[device.Device.Id] = new TrustedDevice(device.Device.Id, user, device.Device.Key, device.IsRemoved);
        foreach (DeviceId omitted in applied.Omitted)
            devices.Remove(omitted);
        rosters[user] = (roster, applied.RemovedEver);
        return this;
    }

    public bool TryGetManifest(PackageId package, [NotNullWhen(true)] out VerifiedManifest? manifest) =>
        manifests.TryGetValue(package, out manifest);

    public bool TryGetGrant(PackageId package, UserId grantee, [NotNullWhen(true)] out VerifiedGrant? grant) =>
        grants.TryGetValue((package, grantee), out grant);

    public bool TryGetDevice(DeviceId device, out TrustedDevice trusted) => devices.TryGetValue(device, out trusted);
}
