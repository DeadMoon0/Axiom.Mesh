using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// What the entering side of a pairing was given (PAIR-SPEC.md §2, §2.4): where the showing side listens, and the
/// device it pins. A target comes from a code, which names the showing side's device key, or from a discovery
/// announcement, which names its device id; either way the pin is the device id, and the TLS handshake under
/// <c>axiom-mesh-pair/1</c> is accepted only from a key whose hash is that id (§4.2). The SAS (§8) stays the check
/// against a man in the middle whichever way the device was named.
/// </summary>
public sealed class PairingTarget
{
    private readonly PairingAddress[] addresses;

    private PairingTarget(DeviceId device, PairingAddress[] addresses, PairingCode? code)
    {
        Device = device;
        this.addresses = addresses;
        Code = code;
    }

    /// <summary>The device the entering side pins: <c>H</c> of the key the handshake must present.</summary>
    public DeviceId Device { get; }

    /// <summary>Where the showing side listens, in the order to try (§2.2, §4.1).</summary>
    public IReadOnlyList<PairingAddress> Addresses => addresses;

    /// <summary>The code the target was made from; <see langword="null"/> when the device was named by its id.</summary>
    public PairingCode? Code { get; }

    /// <summary>Whether the target came from a code, which named the device's key, rather than from its id.</summary>
    public bool IsByCode => Code is not null;

    /// <summary>The target a code names: the code's addresses, and its device key pinned by its hash.</summary>
    /// <param name="code">The code the user entered.</param>
    /// <returns>The target.</returns>
    public static PairingTarget OfCode(PairingCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return new PairingTarget(code.DeviceKey.AsDeviceId(), [.. code.Addresses], code);
    }

    /// <summary>The target a discovery announcement names: the device id it claims, at the addresses it was heard at (§2.4).</summary>
    /// <param name="device">The device id discovery announced; never all zero, which names no device.</param>
    /// <param name="addresses">Where it listens: 1 to 8 well-formed addresses, in the order to try.</param>
    /// <returns>The target.</returns>
    /// <exception cref="ArgumentException">The id is all zero, an address is malformed, or there are none or too many.</exception>
    public static PairingTarget OfDevice(DeviceId device, IReadOnlyList<PairingAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        if (device.Value.IsZero)
            throw new ArgumentException("A device id of all zeros names no device (PAIR-SPEC.md §2.4).", nameof(device));
        if (addresses.Count is 0 or > PairingCode.MaxAddresses)
            throw new ArgumentException($"A target lists 1 to {PairingCode.MaxAddresses} addresses, not {addresses.Count} (PAIR-SPEC.md §2.2).", nameof(addresses));
        if (addresses.FirstOrDefault(a => !a.IsWellFormed) is { Host: not null } malformed)
            throw new ArgumentException($"Address '{malformed}' is not a host and a port (PAIR-SPEC.md §2.2).", nameof(addresses));
        return new PairingTarget(device, [.. addresses], null);
    }

    /// <summary>Whether a key the TLS handshake presented is the pinned device's (§4.2): its hash is <see cref="Device"/>.</summary>
    /// <param name="presented">The subject public key of the certificate the showing side presented.</param>
    /// <returns><see langword="true"/> when the handshake may complete.</returns>
    public bool Admits(PublicKey presented)
    {
        ArgumentNullException.ThrowIfNull(presented);
        return presented.AsDeviceId() == Device;
    }

    /// <summary>Whether an encoded key the TLS handshake presented is the pinned device's: it is a P-256 point whose hash is <see cref="Device"/>.</summary>
    /// <param name="point">The subject public key as the certificate encodes it.</param>
    /// <param name="key">The key, when admitted; else <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the handshake may complete.</returns>
    public bool Admits(ReadOnlySpan<byte> point, out PublicKey? key)
    {
        key = PublicKey.TryParse(point, out PublicKey? parsed) && Admits(parsed) ? parsed : null;
        return key is not null;
    }

    /// <inheritdoc />
    public override string ToString() => IsByCode ? Code!.Text : $"{Device} at {string.Join(",", addresses)}";
}
