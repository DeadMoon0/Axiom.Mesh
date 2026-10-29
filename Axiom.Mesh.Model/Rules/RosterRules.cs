using System;
using System.Collections.Generic;

namespace Axiom.Mesh;

/// <summary>Which devices of a user's current Roster a node treats as removed (SPEC.md §8.8).</summary>
public static class RosterRules
{
    /// <summary>
    /// Applies a new current Roster of a user:
    /// <list type="bullet">
    /// <item>removal is permanent: a device marked removed in any Roster the node verified, or omitted by a later one,
    /// is removed in every later Roster whatever its flag;</item>
    /// <item>an omitted device counts as removed;</item>
    /// <item>a device id that another user's current Roster (verified earlier) lists is removed in this, the later,
    /// Roster.</item>
    /// </list>
    /// </summary>
    /// <param name="previous">The user's Roster held as current so far, or <see langword="null"/>.</param>
    /// <param name="next">The newly verified Roster that supersedes it.</param>
    /// <param name="removedEver">The user's devices removed or omitted in any Roster verified before.</param>
    /// <param name="heldByOtherUsers">The device ids the current Rosters of other users list.</param>
    /// <returns>The effective devices and the updated removal record.</returns>
    /// <exception cref="ArgumentException"><paramref name="previous"/> is another user's, or <paramref name="next"/> does not supersede it.</exception>
    public static RosterApplication Apply(VerifiedRoster? previous, VerifiedRoster next, IReadOnlySet<DeviceId> removedEver, IReadOnlySet<DeviceId> heldByOtherUsers)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(removedEver);
        ArgumentNullException.ThrowIfNull(heldByOtherUsers);
        if (previous is not null && previous.Content.User != next.Content.User)
            throw new ArgumentException($"The previous Roster is user {previous.Content.User}'s, the next one {next.Content.User}'s.", nameof(previous));
        if (previous is not null && !CurrentObject.Supersedes(next.Content.Stamp, previous.Content.Stamp))
            throw new ArgumentException("The next Roster does not supersede the previous one (SPEC.md §8.8); keep the previous.", nameof(next));

        var omitted = new List<DeviceId>();
        foreach (RosterDevice device in previous?.Content.Devices ?? [])
        {
            if (!next.Content.TryGetDevice(device.Id, out _))
                omitted.Add(device.Id);
        }

        var removed = new HashSet<DeviceId>(removedEver);
        removed.UnionWith(omitted);
        var devices = new EffectiveDevice[next.Content.Devices.Count];
        for (int i = 0; i < devices.Length; i++)
        {
            RosterDevice device = next.Content.Devices[i];
            DeviceRemoval removal =
                device.Removed ? DeviceRemoval.Flagged
                : removed.Contains(device.Id) ? DeviceRemoval.RemovedBefore
                : heldByOtherUsers.Contains(device.Id) ? DeviceRemoval.HeldByAnotherUser
                : DeviceRemoval.None;
            if (device.Removed)
                removed.Add(device.Id);
            devices[i] = new EffectiveDevice(device, removal);
        }

        return new RosterApplication(devices, omitted, removed);
    }
}
