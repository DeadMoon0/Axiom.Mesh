using System.Collections.Generic;

namespace Axiom.Mesh;

/// <summary>What a node holds for a user once a new current Roster is applied (<see cref="RosterRules.Apply"/>).</summary>
public sealed class RosterApplication
{
    internal RosterApplication(IReadOnlyList<EffectiveDevice> devices, IReadOnlyList<DeviceId> omitted, IReadOnlyCollection<DeviceId> removedEver)
    {
        Devices = devices;
        Omitted = omitted;
        RemovedEver = removedEver;
    }

    /// <summary>Every device the new Roster lists, ascending by id, with whether the node treats it as removed.</summary>
    public IReadOnlyList<EffectiveDevice> Devices { get; }

    /// <summary>Devices the previous Roster listed and the new one omits: removed, and worth a log line (SPEC.md §8.8).</summary>
    public IReadOnlyList<DeviceId> Omitted { get; }

    /// <summary>Every device of the user ever removed or omitted in a Roster the node verified, to keep for the next one.</summary>
    public IReadOnlyCollection<DeviceId> RemovedEver { get; }
}
