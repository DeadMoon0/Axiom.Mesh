namespace Axiom.Mesh.Protocol;

/// <summary>
/// The other side of a pairing as its <c>PairHello</c> proved it (PAIR-SPEC.md §7.1): made only by
/// <see cref="PairingRules.TryAcceptHello"/>.
/// </summary>
public sealed class PairPeer
{
    internal PairPeer(PairingMode mode, PublicKey tlsKey, string name, VerifiedRoster? roster)
    {
        Mode = mode;
        TlsKey = tlsKey;
        Name = name;
        Roster = roster;
    }

    /// <summary>The other side's mode.</summary>
    public PairingMode Mode { get; }

    /// <summary>The other side's device key, as its TLS certificate presented it.</summary>
    public PublicKey TlsKey { get; }

    /// <summary>The other side's device id, <c>H</c> of <see cref="TlsKey"/>.</summary>
    public DeviceId Device => TlsKey.AsDeviceId();

    /// <summary>The other side's display name.</summary>
    public string Name { get; }

    /// <summary>
    /// The other side's user's current Roster, verified under its own user key and listing <see cref="Device"/> as not
    /// removed; <see langword="null"/> for a device being enrolled.
    /// </summary>
    public VerifiedRoster? Roster { get; }

    /// <summary>The other side's user key; <see langword="null"/> for a device being enrolled.</summary>
    public PublicKey? UserKey => Roster?.Content.UserKey;

    /// <inheritdoc />
    public override string ToString() => $"{Mode} {Device} \"{Name}\"";
}
