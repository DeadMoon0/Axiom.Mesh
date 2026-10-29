using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// The checks a side makes on what the other side of a pairing sends (PAIR-SPEC.md §7), and the Roster that enrols a
/// device. Pure functions of the messages, the keys the TLS handshake proved, and what this side holds.
/// </summary>
public static class PairingRules
{
    /// <summary>
    /// Checks the other side's <c>PairHello</c>, in this order: <c>version</c> is 1; its mode is the counterpart of this
    /// side's (ShowUser with JoinUser, ShowDevice with AddDevice); <c>deviceId</c> is <c>H</c> of its TLS key; and
    /// unless it is the device being enrolled, its Roster parses, verifies under its own user key, is that user key's,
    /// and lists <c>deviceId</c> as not removed.
    /// </summary>
    /// <param name="hello">The other side's <c>PairHello</c>.</param>
    /// <param name="peerTlsKey">The key of the other side's TLS certificate; the entering side pinned it to the code.</param>
    /// <param name="ownMode">This side's mode.</param>
    /// <param name="peer">The other side, proved; or <see langword="null"/>.</param>
    /// <param name="refusal">The <c>PairCancel(Refused)</c> to send when a check fails; or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when every check passed.</returns>
    public static bool TryAcceptHello(
        PairHello hello, PublicKey peerTlsKey, PairingMode ownMode,
        [NotNullWhen(true)] out PairPeer? peer, [NotNullWhen(false)] out PairCancel? refusal)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(peerTlsKey);
        peer = null;

        string? problem = hello.Version != PairProtocol.Version ? $"version {hello.Version} is not {PairProtocol.Version}"
            : hello.Mode != Counterpart(ownMode) ? $"mode {hello.Mode} does not pair with {ownMode}"
            : hello.Device != peerTlsKey.AsDeviceId() ? "deviceId is not H(TLS key)"
            : null;
        VerifiedRoster? roster = null;
        if (problem is null && hello.Mode == PairingMode.ShowDevice)
            problem = hello.UserKey is null && hello.Roster.IsEmpty ? null : "a device being enrolled sends no user key and no roster";
        else if (problem is null && !TryVerifyRoster(hello.Roster, out roster))
            problem = "the roster is not a valid roster";
        else if (problem is null && !roster!.Content.UserKey.Equals(hello.UserKey))
            problem = "the roster is not the user key's";
        else if (problem is null && !(roster!.Content.TryGetDevice(hello.Device, out RosterDevice? listed) && !listed.Removed))
            problem = "the roster does not list deviceId as a current device";

        refusal = problem is null ? null : new PairCancel(PairCancelReason.Refused, problem);
        if (refusal is null)
            peer = new PairPeer(hello.Mode, peerTlsKey, hello.Name, roster);
        return refusal is null;
    }

    /// <summary>
    /// Checks the Roster that enrols this device (mode <see cref="PairingMode.ShowDevice"/>): it parses, verifies under
    /// the user key the other side's <c>PairHello</c> proved, has a higher <c>seq</c> than the Roster that came with it,
    /// and lists this device's key as not removed (PAIR-SPEC.md §7.2).
    /// </summary>
    /// <param name="confirm">The <c>PairConfirm</c> of the <see cref="PairingMode.AddDevice"/> side.</param>
    /// <param name="userSide">That side, as <see cref="TryAcceptHello"/> proved it.</param>
    /// <param name="ownDeviceKey">This device's key.</param>
    /// <param name="roster">The Roster to adopt; or <see langword="null"/>.</param>
    /// <param name="refusal">The <c>PairCancel(Refused)</c> to send when a check fails; or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the Roster enrols this device.</returns>
    public static bool TryAcceptEnrolment(
        PairConfirm confirm, PairPeer userSide, PublicKey ownDeviceKey,
        [NotNullWhen(true)] out VerifiedRoster? roster, [NotNullWhen(false)] out PairCancel? refusal)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(userSide);
        ArgumentNullException.ThrowIfNull(ownDeviceKey);
        if (userSide.Roster is null)
            throw new ArgumentException("An enrolment comes from the AddDevice side, which sent a Roster.", nameof(userSide));

        string? problem = !TryVerifyRoster(confirm.Roster, out roster) ? "the enrolment carries no valid roster"
            : !roster.Content.UserKey.Equals(userSide.Roster.Content.UserKey) ? "the roster is not the user key's"
            : roster.Content.Seq <= userSide.Roster.Content.Seq ? "the roster is not newer than the user's current one"
            : !(roster.Content.TryGetDevice(ownDeviceKey.AsDeviceId(), out RosterDevice? listed) && !listed.Removed && listed.Key.Equals(ownDeviceKey))
                ? "the roster does not list this device as a current device"
            : null;

        refusal = problem is null ? null : new PairCancel(PairCancelReason.Refused, problem);
        if (refusal is not null)
            roster = null;
        return refusal is null;
    }

    /// <summary>
    /// The Roster that enrols a device (mode <see cref="PairingMode.AddDevice"/>): the current one with <c>seq</c> one
    /// higher and the device added, or kept when it is already a current device. A device once removed is never added
    /// again (SPEC.md §8.8).
    /// </summary>
    /// <param name="current">This user's current Roster.</param>
    /// <param name="device">The device being enrolled, as <see cref="TryAcceptHello"/> proved it.</param>
    /// <param name="removedEver">Every device this user's Rosters ever removed or omitted, as the trust snapshot keeps them.</param>
    /// <param name="addedAt">When the device is added, milliseconds since the Unix epoch.</param>
    /// <param name="draft">The Roster to sign with the user key; or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the device was removed before and so cannot be enrolled.</returns>
    public static bool TryEnrol(VerifiedRoster current, PairPeer device, IReadOnlySet<DeviceId> removedEver, ulong addedAt, [NotNullWhen(true)] out RosterDraft? draft)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(removedEver);
        draft = null;
        bool listed = current.Content.TryGetDevice(device.Device, out RosterDevice? existing);
        if (removedEver.Contains(device.Device) || (listed && existing!.Removed))
            return false;

        var devices = new List<RosterDevice>(current.Content.Devices);
        if (!listed)
            devices.Add(new RosterDevice(device.TlsKey, device.Name, addedAt, removed: false));
        draft = new RosterDraft { Seq = current.Content.Seq + 1, Devices = devices };
        return true;
    }

    /// <summary>The mode the other side of a pairing must be in.</summary>
    /// <param name="mode">This side's mode.</param>
    /// <returns>The counterpart, or <see cref="PairingMode.None"/> for an undefined mode.</returns>
    public static PairingMode Counterpart(PairingMode mode) => mode switch
    {
        PairingMode.ShowUser => PairingMode.JoinUser,
        PairingMode.JoinUser => PairingMode.ShowUser,
        PairingMode.ShowDevice => PairingMode.AddDevice,
        PairingMode.AddDevice => PairingMode.ShowDevice,
        _ => PairingMode.None,
    };

    // The roster bytes are the message's own copy, so the parsed objects may refer to them.
    private static bool TryVerifyRoster(ReadOnlyMemory<byte> bytes, [NotNullWhen(true)] out VerifiedRoster? roster)
    {
        roster = null;
        return !bytes.IsEmpty
            && Objects.TryParse(HashedObject.Of(bytes), out ParsedObject? parsed, out _)
            && parsed is UnverifiedRoster unverified
            && VerifiedRoster.TryVerify(unverified, out roster);
    }
}
