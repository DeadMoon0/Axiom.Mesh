using System;

namespace Axiom.Mesh;

/// <summary>One device of a Roster (SPEC.md §8.8). Its id is always <c>H(key)</c>: it is derived, never given.</summary>
public sealed class RosterDevice
{
    /// <summary>A device as a Roster lists it.</summary>
    /// <param name="key">The device key.</param>
    /// <param name="name">The device's name; at most 64 UTF-8 bytes.</param>
    /// <param name="addedAt">Milliseconds since the Unix epoch when it was added; informational.</param>
    /// <param name="removed">Whether the user removed it; removal is permanent (SPEC.md §8.8).</param>
    public RosterDevice(PublicKey key, string name, ulong addedAt, bool removed)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(name);
        Key = key;
        Name = name;
        AddedAt = addedAt;
        Removed = removed;
    }

    /// <summary>The device id, <c>H(Key)</c>.</summary>
    public DeviceId Id => Key.AsDeviceId();

    /// <summary>The device key.</summary>
    public PublicKey Key { get; }

    /// <summary>The device's name.</summary>
    public string Name { get; }

    /// <summary>Milliseconds since the Unix epoch when it was added; informational.</summary>
    public ulong AddedAt { get; }

    /// <summary>The flag as this Roster states it; see <see cref="RosterRules"/> for what a node treats as removed.</summary>
    public bool Removed { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Id}){(Removed ? ", removed" : string.Empty)}";
}
