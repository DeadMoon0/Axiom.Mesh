using System;
using Axiom.Mesh.Discovery;

namespace Axiom.Mesh.Core;

/// <summary>
/// Whether to dial a device discovery announced (SPEC.md §3, §4.4): only a not-removed device of a known user's current
/// Roster, never this node itself, never while a session to it exists or opens, never during a backoff. A static
/// peer's announcement names no device and is decided by <see cref="DecideStatic"/>: its address is dialled and the
/// handshake decides. Pure: the trust, the peer's standing and the time are handed in.
/// </summary>
internal static class DialPolicy
{
    /// <summary>Decides for an announcement of mDNS or broadcast.</summary>
    /// <param name="announcement">The claim; never one of <see cref="DiscoverySource.Static"/>.</param>
    /// <param name="trust">The node's trust now.</param>
    /// <param name="self">This node's device, or <see langword="null"/> before it has an identity.</param>
    /// <param name="state">The device's state in the peer table; <see cref="PeerState.Discovered"/> when it has none.</param>
    /// <param name="backoffUntil">When the device's backoff ends, if it has one.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Dial, or skip with the reason.</returns>
    /// <exception cref="ArgumentException">The announcement is a static peer's.</exception>
    public static DialDecision Decide(Announcement announcement, ITrustSnapshotView trust, DeviceId? self, PeerState state, DateTimeOffset? backoffUntil, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(announcement);
        ArgumentNullException.ThrowIfNull(trust);
        if (announcement.Source.HasFlag(DiscoverySource.Static))
            throw new ArgumentException("A static peer names no device; decide it with DecideStatic.", nameof(announcement));
        if (self is not { } own)
            return DialDecision.Skip(SkipReason.NoIdentity);
        if (announcement.Device == own)
            return DialDecision.Skip(SkipReason.Self);
        if (!trust.TryGetDevice(announcement.Device, out TrustedDevice device))
            return DialDecision.Skip(SkipReason.UnknownDevice);
        if (device.IsRemoved)
            return DialDecision.Skip(SkipReason.Removed);
        return Standing(state, backoffUntil, now);
    }

    /// <summary>Decides for a static peer's address, which names no device (SPEC.md §3.3).</summary>
    /// <param name="hasIdentity">Whether this node has an identity.</param>
    /// <param name="state">Where the address stands: dialling, connected through its device, backed off, or resting.</param>
    /// <param name="backoffUntil">When the address's backoff ends, if it has one.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Dial, or skip with the reason.</returns>
    public static DialDecision DecideStatic(bool hasIdentity, PeerState state, DateTimeOffset? backoffUntil, DateTimeOffset now) =>
        hasIdentity ? Standing(state, backoffUntil, now) : DialDecision.Skip(SkipReason.NoIdentity);

    private static DialDecision Standing(PeerState state, DateTimeOffset? backoffUntil, DateTimeOffset now) => state switch
    {
        PeerState.Connected or PeerState.Established => DialDecision.Skip(SkipReason.Connected),
        PeerState.Dialling => DialDecision.Skip(SkipReason.Dialling),
        _ when backoffUntil is { } until && now < until => DialDecision.Skip(SkipReason.BackingOff),
        _ => DialDecision.Dial,
    };
}
