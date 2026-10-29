using System.Collections.Generic;

namespace Axiom.Mesh.Core;

/// <summary>What the node holds of one user: the key, whether it was paired, the current Roster and its effect (SPEC.md §8.8).</summary>
internal sealed class UserTrust
{
    /// <summary>A user as the node holds it.</summary>
    /// <param name="key">The user key.</param>
    /// <param name="paired">Known through pairing, or the own user.</param>
    /// <param name="roster">The current Roster, or <see langword="null"/>.</param>
    /// <param name="devices">The current Roster's devices with their removal, ascending by id.</param>
    /// <param name="removedEver">Every device of the user removed or omitted in a Roster the node verified.</param>
    public UserTrust(PublicKey key, bool paired, VerifiedRoster? roster, IReadOnlyList<EffectiveDevice> devices, IReadOnlySet<DeviceId> removedEver)
    {
        Key = key;
        Paired = paired;
        Roster = roster;
        Devices = devices;
        RemovedEver = removedEver;
    }

    /// <summary>The user id.</summary>
    public UserId Id => Key.AsUserId();

    /// <summary>The user key.</summary>
    public PublicKey Key { get; }

    /// <summary>Known through pairing, or the own user: known whatever Packages say (SPEC.md §1.2).</summary>
    public bool Paired { get; }

    /// <summary>The current Roster, or <see langword="null"/> while none is stored.</summary>
    public VerifiedRoster? Roster { get; }

    /// <summary>The current Roster's devices, with whether the node treats each as removed (<see cref="RosterRules"/>).</summary>
    public IReadOnlyList<EffectiveDevice> Devices { get; }

    /// <summary>Every device ever removed or omitted: removed in every later Roster (SPEC.md §8.8).</summary>
    public IReadOnlySet<DeviceId> RemovedEver { get; }
}
