using Axiom.Mesh.Local;
using Axiom.Mesh.Protocol;
using LocalMode = Axiom.Mesh.Local.PairingMode;

namespace Axiom.Mesh.Core;

/// <summary>
/// LOCAL-SPEC.md §6.2 <c>StartPairing</c>, the checks of its mode, as a function of trust: <c>ShowUser</c> and
/// <c>JoinUser</c> need an identity, <c>AddDevice</c> the user key too, <c>ShowDevice</c> no identity
/// (<c>AlreadyExists</c>); the entering modes need a well-formed code of the matching kind (PAIR-SPEC.md §2.3), the
/// showing modes none. A code that shows a device of the own user is refused: pairing with one's own user is enrolment
/// (a decision PAIR-SPEC.md leaves open).
/// </summary>
internal static class PairingStartRule
{
    /// <summary>Decides a <c>StartPairing</c>; the one-pairing-at-a-time rule is the machine's (<c>Busy</c>).</summary>
    /// <param name="mode">The mode asked for.</param>
    /// <param name="code">The code given; empty for the showing modes.</param>
    /// <param name="trust">The node's trust.</param>
    /// <param name="userKeyHeld">Whether the user key is on this device.</param>
    /// <param name="entered">The code parsed, for the entering modes; else <see langword="null"/>.</param>
    /// <returns><see langword="null"/> when the pairing may start; else why not.</returns>
    public static RequestRefusal? Check(LocalMode mode, string code, TrustSnapshot trust, bool userKeyHeld, out PairingCode? entered)
    {
        entered = null;
        if (mode is < LocalMode.ShowUser or > LocalMode.AddDevice)
            return new RequestRefusal(Status.Invalid, $"Mode {(byte)mode} is not a pairing mode: 1 ShowUser, 2 JoinUser, 3 ShowDevice, 4 AddDevice.", "mode");

        if (mode == LocalMode.ShowDevice)
        {
            return trust.Self is not null ? new RequestRefusal(Status.AlreadyExists, "This device has an identity already; enrol a new device from here with mode AddDevice instead.", "mode")
                : code.Length != 0 ? new RequestRefusal(Status.Invalid, "A showing side shows its own code and takes none.", "code")
                : null;
        }

        if (trust.Self is not { } self)
            return new RequestRefusal(Status.NoIdentity, "Pairing needs an identity: create one, or enrol this device from another device of the user.", string.Empty);
        if (mode == LocalMode.AddDevice && !userKeyHeld)
            return new RequestRefusal(Status.UserKeyAbsent, "Adding a device needs the user key, which is on another device of this user: add the device there.", string.Empty);
        if (mode == LocalMode.ShowUser)
            return code.Length != 0 ? new RequestRefusal(Status.Invalid, "A showing side shows its own code and takes none.", "code") : null;

        if (!PairingCode.TryParse(code, out PairingCode? parsed))
            return new RequestRefusal(Status.Invalid, "The code is not a pairing code (amp1:<mode>:<addresses>:<key>); copy it exactly as the other side shows it.", "code");
        if ((byte)parsed.EnteringMode != (byte)mode)
        {
            return new RequestRefusal(
                Status.Invalid,
                mode == LocalMode.JoinUser ? "The code is a device's code: enter it with mode AddDevice." : "The code is a user's code: enter it with mode JoinUser.",
                "code");
        }

        if (mode == LocalMode.JoinUser && IsOwnDevice(trust, self, parsed))
            return new RequestRefusal(Status.Invalid, "The code shows a device of this user; a device of the own user is enrolled (AddDevice), not paired.", "code");

        entered = parsed;
        return null;
    }

    private static bool IsOwnDevice(TrustSnapshot trust, SelfIdentity self, PairingCode code) =>
        trust.TryGetDevice(code.DeviceKey.AsDeviceId(), out TrustedDevice device) && device.User == self.User;
}
