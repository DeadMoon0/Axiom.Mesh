using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>PairHello</c> (1, PAIR-SPEC.md §5.2): the first frame each side of a pairing sends, naming its mode, its device,
/// a display name, and, unless it is the device being enrolled, its user key and its user's current Roster.
/// </summary>
/// <remarks>
/// The codec reads any version and mode value it can represent; <see cref="PairingRules.TryAcceptHello"/> checks them.
/// A user key of 65 zero bytes stands for "no user" (mode <see cref="PairingMode.ShowDevice"/>).
/// </remarks>
public sealed class PairHello : PairMessage, IWireMessage<PairHello>
{
    private readonly byte[] roster;

    /// <summary>A <c>PairHello</c> of version 1.</summary>
    /// <param name="mode">The sender's mode.</param>
    /// <param name="device">The sender's device id, <c>H</c> of its TLS key.</param>
    /// <param name="name">A display name, at most 64 UTF-8 bytes: the device's in <see cref="PairingMode.ShowDevice"/>, the user's otherwise.</param>
    /// <param name="userKey">The sender's user key; <see langword="null"/> exactly in <see cref="PairingMode.ShowDevice"/>.</param>
    /// <param name="roster">The sender's user's current Roster object; empty exactly in <see cref="PairingMode.ShowDevice"/>.</param>
    /// <exception cref="ArgumentException">The mode is undefined, the name too long, or the user key and Roster do not match the mode.</exception>
    public PairHello(PairingMode mode, DeviceId device, string name, PublicKey? userKey, ReadOnlySpan<byte> roster)
        : this(PairProtocol.Version, mode, device, name, userKey, roster.ToArray())
    {
        if (mode is < PairingMode.ShowUser or > PairingMode.AddDevice)
            throw new ArgumentException($"Mode {(byte)mode} is not a pairing mode (LOCAL-SPEC.md §14.10).", nameof(mode));
        Wire.StringLength(name, Limits.MaxDeviceNameLength, nameof(name));
        bool deviceSide = mode == PairingMode.ShowDevice;
        if (deviceSide != (userKey is null) || deviceSide != roster.IsEmpty)
            throw new ArgumentException("A user key and a Roster are sent exactly when the mode is not ShowDevice (PAIR-SPEC.md §5.2).", nameof(userKey));
        if (roster.Length > Limits.MaxObjectLength)
            throw new ArgumentException($"A Roster of {roster.Length} bytes exceeds an object's 4 MiB (SPEC.md §8.1).", nameof(roster));
    }

    private PairHello(ushort version, PairingMode mode, DeviceId device, string name, PublicKey? userKey, byte[] roster)
    {
        Version = version;
        Mode = mode;
        Device = device;
        Name = name;
        UserKey = userKey;
        this.roster = roster;
    }

    /// <summary>The pairing protocol version; 1 for this one.</summary>
    public ushort Version { get; }

    /// <summary>The sender's mode.</summary>
    public PairingMode Mode { get; }

    /// <summary>The sender's device id.</summary>
    public DeviceId Device { get; }

    /// <summary>The sender's display name.</summary>
    public string Name { get; }

    /// <summary>The sender's user key; <see langword="null"/> for a device being enrolled.</summary>
    public PublicKey? UserKey { get; }

    /// <summary>The sender's user's current Roster object, as sent; empty for a device being enrolled.</summary>
    public ReadOnlyMemory<byte> Roster => roster;

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out PairHello message)
    {
        ushort version = reader.UInt16BigEndian();
        var mode = (PairingMode)reader.UInt8();
        DeviceId device = DeviceId.Read(ref reader);
        string name = Wire.ReadString(ref reader, Limits.MaxDeviceNameLength);
        PublicKey? userKey = PairWire.ReadOptionalKey(ref reader);
        byte[] roster = Wire.ReadBlob(ref reader, Limits.MaxObjectLength).ToArray();
        message = new PairHello(version, mode, device, name, userKey, reader.Ok ? roster : []);
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt16BigEndian(Version);
        writer.UInt8((byte)Mode);
        Device.Write(ref writer);
        Wire.WriteString(ref writer, Name, Limits.MaxDeviceNameLength);
        PairWire.WriteOptionalKey(ref writer, UserKey);
        Wire.WriteBlob(ref writer, roster, Limits.MaxObjectLength);
    }

    /// <inheritdoc />
    public override string ToString() => $"PairHello v{Version} {Mode} device {Device} \"{Name}\"";
}
