using System.Collections.Generic;
using Axiom.Mesh.Local;
using Axiom.Mesh.Protocol;
using LocalMode = Axiom.Mesh.Local.PairingMode;

namespace Axiom.Mesh.Core;

/// <summary>
/// LOCAL-SPEC.md §6.2 <c>StartPairing</c> and <c>StartPairingByDevice</c>, the checks of their mode, as a function of
/// trust: <c>ShowUser</c> and <c>JoinUser</c> need an identity, <c>AddDevice</c> the user key too, <c>ShowDevice</c> no
/// identity (<c>AlreadyExists</c>); the entering modes need a target, the showing modes none. A target is a well-formed
/// code of the matching kind (PAIR-SPEC.md §2.3), or a device id with 1 to 8 well-formed addresses as discovery
/// announced them (PAIR-SPEC.md §2.4). A target that is a device of the own user is refused: pairing with one's own user
/// is enrolment (a decision PAIR-SPEC.md leaves open).
/// </summary>
internal static class PairingStartRule
{
    /// <summary>Decides a start; the one-pairing-at-a-time rule is the machine's (<c>Busy</c>).</summary>
    /// <param name="start">What the request asked for.</param>
    /// <param name="trust">The node's trust.</param>
    /// <param name="userKeyHeld">Whether the user key is on this device.</param>
    /// <param name="entered">The target, for the entering modes; else <see langword="null"/>.</param>
    /// <returns><see langword="null"/> when the pairing may start; else why not.</returns>
    public static RequestRefusal? Check(PairingStart start, TrustSnapshot trust, bool userKeyHeld, out PairingTarget? entered)
    {
        entered = null;
        LocalMode mode = start.Mode;
        if (mode is < LocalMode.ShowUser or > LocalMode.AddDevice)
            return new RequestRefusal(Status.Invalid, $"Mode {(byte)mode} is not a pairing mode: 1 ShowUser, 2 JoinUser, 3 ShowDevice, 4 AddDevice.", "mode");

        if (mode == LocalMode.ShowDevice)
        {
            return trust.Self is not null ? new RequestRefusal(Status.AlreadyExists, "This device has an identity already; enrol a new device from here with mode AddDevice instead.", "mode")
                : start.ByDevice ? ShowingNamesNoDevice()
                : start.Code.Length != 0 ? new RequestRefusal(Status.Invalid, "A showing side shows its own code and takes none.", "code")
                : null;
        }

        if (trust.Self is not { } self)
            return new RequestRefusal(Status.NoIdentity, "Pairing needs an identity: create one, or enrol this device from another device of the user.", string.Empty);
        if (mode == LocalMode.AddDevice && !userKeyHeld)
            return new RequestRefusal(Status.UserKeyAbsent, "Adding a device needs the user key, which is on another device of this user: add the device there.", string.Empty);
        if (mode == LocalMode.ShowUser)
        {
            return start.ByDevice ? ShowingNamesNoDevice()
                : start.Code.Length != 0 ? new RequestRefusal(Status.Invalid, "A showing side shows its own code and takes none.", "code")
                : null;
        }

        RequestRefusal? refused = start.ByDevice
            ? CheckDevice(start.Device, start.Addresses, out entered)
            : CheckCode(mode, start.Code, out entered);
        if (refused is not null)
            return refused;
        if (mode == LocalMode.JoinUser && IsOwnDevice(trust, self, entered!.Device))
        {
            entered = null;
            return new RequestRefusal(Status.Invalid, "The target is a device of this user; a device of the own user is enrolled (AddDevice), not paired.", start.ByDevice ? "device" : "code");
        }

        return null;
    }

    private static RequestRefusal ShowingNamesNoDevice() =>
        new(Status.Invalid, "A showing side names no device: StartPairingByDevice enters, in mode JoinUser or AddDevice.", "mode");

    // PAIR-SPEC.md §2.3: exactly the written form, of the kind the mode enters.
    private static RequestRefusal? CheckCode(LocalMode mode, string code, out PairingTarget? entered)
    {
        entered = null;
        if (!PairingCode.TryParse(code, out PairingCode? parsed))
            return new RequestRefusal(Status.Invalid, "The code is not a pairing code (amp1:<mode>:<addresses>:<key>); copy it exactly as the other side shows it.", "code");
        if ((byte)parsed.EnteringMode != (byte)mode)
        {
            return new RequestRefusal(
                Status.Invalid,
                mode == LocalMode.JoinUser ? "The code is a device's code: enter it with mode AddDevice." : "The code is a user's code: enter it with mode JoinUser.",
                "code");
        }

        entered = PairingTarget.OfCode(parsed);
        return null;
    }

    // PAIR-SPEC.md §2.4: a device id that names a device, at 1 to 8 well-formed addresses.
    private static RequestRefusal? CheckDevice(DeviceId device, IReadOnlyList<string> addresses, out PairingTarget? entered)
    {
        entered = null;
        if (device.Value.IsZero)
            return new RequestRefusal(Status.Invalid, "A device id of all zeros names no device; give the id ListDiscovered shows.", "device");
        if (addresses.Count is 0 or > PairingCode.MaxAddresses)
            return new RequestRefusal(Status.Invalid, $"A device named by its id needs 1 to {PairingCode.MaxAddresses} addresses to dial, as ListDiscovered shows them; {addresses.Count} were given.", "addresses");
        var parsed = new PairingAddress[addresses.Count];
        for (int i = 0; i < parsed.Length; i++)
        {
            if (!PairingAddress.TryParse(addresses[i], out parsed[i]))
                return new RequestRefusal(Status.Invalid, $"Address '{addresses[i]}' is not host:port or [ipv6]:port.", "addresses");
        }

        entered = PairingTarget.OfDevice(device, parsed);
        return null;
    }

    private static bool IsOwnDevice(TrustSnapshot trust, SelfIdentity self, DeviceId device) =>
        trust.TryGetDevice(device, out TrustedDevice trusted) && trusted.User == self.User;
}
