using System;

namespace Axiom.Mesh.Protocol;

/// <summary>The checks a node makes on its peer's <c>Hello</c> (SPEC.md §7.1).</summary>
public static class HelloRule
{
    /// <summary>
    /// Checks a <c>Hello</c>, in this order: <c>version</c> is 1; <c>deviceId</c> is <c>H</c> of the peer's TLS key; the
    /// device is in a current Roster of a known user, and that user is <c>userId</c>; the device is not removed. Then
    /// compares the peer's <c>rosterSeq</c> with the one this node holds.
    /// </summary>
    /// <param name="hello">The peer's <c>Hello</c>.</param>
    /// <param name="peer">What the TLS handshake proved, and the Roster seq this node holds for the peer's user.</param>
    /// <param name="trust">The node's trust: only devices of known users' current Rosters resolve.</param>
    /// <returns><see cref="HelloOutcome.Ok"/>, or <see cref="HelloOutcome.Refused"/> with <c>Bye(Unauthorized)</c>.</returns>
    public static HelloOutcome Check(Hello hello, PeerIdentityFacts peer, ITrustSnapshotView trust)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(trust);
        if (peer.TlsKey is null)
            throw new ArgumentException("The peer's facts are default: they name no TLS key.", nameof(peer));

        if (hello.Version != Amp.Version)
            return new HelloOutcome.Refused($"version {hello.Version} is not {Amp.Version}");
        if (hello.Device != peer.TlsKey.AsDeviceId())
            return new HelloOutcome.Refused("deviceId is not H(TLS key)");
        if (!trust.TryGetDevice(hello.Device, out TrustedDevice device))
            return new HelloOutcome.Refused("the device is in no current roster of a known user");
        if (device.User != hello.User)
            return new HelloOutcome.Refused("the device is not one of userId's devices");
        if (device.IsRemoved)
            return new HelloOutcome.Refused("the device was removed from its user's roster");

        RosterFollowUp followUp = hello.RosterSeq < peer.HeldRosterSeq ? RosterFollowUp.SendHeldRoster
            : hello.RosterSeq > peer.HeldRosterSeq ? RosterFollowUp.QueryRoster
            : RosterFollowUp.None;
        return new HelloOutcome.Ok(device, followUp);
    }
}
