namespace Axiom.Mesh.Core;

/// <summary>
/// Records what the node saw of a peer device (LOCAL-SPEC.md §5.7): its user once a session proved it, an address it
/// was reached at, a backoff (SPEC.md §4.4), a violation (§12.1, counted per hour).
/// </summary>
internal sealed class PeerCommand : StoreCommand<bool>
{
    /// <summary>A sighting or a verdict about a peer.</summary>
    /// <param name="device">The peer's device.</param>
    /// <param name="user">Its user, when a session proved it.</param>
    /// <param name="address">An address it was seen or reached at, <c>host:port</c>.</param>
    /// <param name="backoffUntilMs">Not dialled before this time, in milliseconds since the Unix epoch; <see langword="null"/> to keep the current one.</param>
    /// <param name="violation">Whether to record a violation against it.</param>
    public PeerCommand(DeviceId device, UserId? user, string? address, long? backoffUntilMs, bool violation)
    {
        Device = device;
        User = user;
        Address = address;
        BackoffUntilMs = backoffUntilMs;
        Violation = violation;
    }

    /// <summary>The peer's device.</summary>
    public DeviceId Device { get; }

    /// <summary>Its user, when proven.</summary>
    public UserId? User { get; }

    /// <summary>An address it was seen or reached at.</summary>
    public string? Address { get; }

    /// <summary>The backoff to set, or <see langword="null"/>.</summary>
    public long? BackoffUntilMs { get; }

    /// <summary>Whether to record a violation.</summary>
    public bool Violation { get; }
}
