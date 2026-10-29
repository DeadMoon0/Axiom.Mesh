using System;

namespace Axiom.Mesh.Discovery;

/// <summary>What this node announces about itself (SPEC.md §3): its device and user ids and the port of its mesh listener.</summary>
public sealed record SelfRecord
{
    private readonly DeviceId device;
    private readonly UserId user;
    private readonly ushort port;

    /// <summary>This node's device id; never all zero, which no key hashes to and which the parsers refuse.</summary>
    /// <exception cref="ArgumentException">The id is all zero.</exception>
    public required DeviceId Device
    {
        get => device;
        init => device = value.Value.IsZero ? throw new ArgumentException("A device id of all zeros names no device; it is what a static peer claims.", nameof(Device)) : value;
    }

    /// <summary>This node's user id; never all zero.</summary>
    /// <exception cref="ArgumentException">The id is all zero.</exception>
    public required UserId User
    {
        get => user;
        init => user = value.Value.IsZero ? throw new ArgumentException("A user id of all zeros names no user; it is what a static peer claims.", nameof(User)) : value;
    }

    /// <summary>The TCP port of the mesh listener (SPEC.md §4.1, default 41810); never 0, which cannot be dialled.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The port is 0.</exception>
    public required ushort Port
    {
        get => port;
        init => port = value == 0 ? throw new ArgumentOutOfRangeException(nameof(Port), "Port 0 cannot be dialled; announce the port the mesh listener is bound to.") : value;
    }
}
