using System;
using System.Collections.Generic;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>Hello</c> (1, SPEC.md §7.1): the first frame each side sends, naming its device, its user, the sequence number of
/// its user's Roster as it holds it, its capabilities and the service protocols it offers.
/// </summary>
/// <remarks>
/// The codec reads any <see cref="Version"/>; <see cref="HelloRule.Check"/> refuses all but 1, as §7.1 says.
/// </remarks>
public sealed class Hello : MeshMessage, IWireMessage<Hello>
{
    private readonly string[] services;

    /// <summary>A <c>Hello</c> of protocol version 1.</summary>
    /// <param name="device">The sender's device id, <c>H</c> of its TLS key.</param>
    /// <param name="user">The sender's user id.</param>
    /// <param name="rosterSeq">The sequence number of the sender's user's Roster, as the sender holds it.</param>
    /// <param name="capabilities">The sender's capability flags; reserved bits must be clear.</param>
    /// <param name="services">The service protocol names the sender offers: at most 64, each at most 64 UTF-8 bytes.</param>
    /// <exception cref="ArgumentException">A reserved capability bit is set, or a service list or name is too long.</exception>
    public Hello(DeviceId device, UserId user, ulong rosterSeq, Capabilities capabilities, IReadOnlyList<string> services)
        : this(Amp.Version, device, user, rosterSeq, capabilities, MessageFields.Own(services, Limits.MaxHelloServices, nameof(services)))
    {
        if ((capabilities & ~KnownCapabilities) != 0)
            throw new ArgumentException($"Capability bits {(uint)(capabilities & ~KnownCapabilities):x} are reserved and must be sent as 0 (SPEC.md §14.3).", nameof(capabilities));
        foreach (string service in this.services)
            Wire.StringLength(service, Limits.MaxServiceNameLength, nameof(services));
    }

    private Hello(ushort version, DeviceId device, UserId user, ulong rosterSeq, Capabilities capabilities, string[] services)
    {
        Version = version;
        Device = device;
        User = user;
        RosterSeq = rosterSeq;
        Capabilities = capabilities;
        this.services = services;
    }

    /// <summary>The flags of SPEC.md §14.3 this version knows; every other bit is reserved.</summary>
    internal const Capabilities KnownCapabilities = Capabilities.FollowsNewGrants | Capabilities.FullMirror;

    /// <inheritdoc />
    public override ushort Id => MessageIds.Hello;

    /// <summary>The protocol version the sender speaks; 1 for this one.</summary>
    public ushort Version { get; }

    /// <summary>The sender's device id.</summary>
    public DeviceId Device { get; }

    /// <summary>The sender's user id.</summary>
    public UserId User { get; }

    /// <summary>The sequence number of the sender's user's Roster, as the sender holds it.</summary>
    public ulong RosterSeq { get; }

    /// <summary>The sender's capability flags; reserved bits received are dropped (§14.3).</summary>
    public Capabilities Capabilities { get; }

    /// <summary>The service protocol names the sender offers (§14.4).</summary>
    public IReadOnlyList<string> Services => services;

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out Hello message)
    {
        ushort version = reader.UInt16BigEndian();
        DeviceId device = DeviceId.Read(ref reader);
        UserId user = UserId.Read(ref reader);
        ulong rosterSeq = reader.UInt64BigEndian();
        var capabilities = (Capabilities)reader.UInt32BigEndian() & KnownCapabilities;
        string[] services = MessageFields.ReadStrings(ref reader, Limits.MaxHelloServices, Limits.MaxServiceNameLength);
        message = new Hello(version, device, user, rosterSeq, capabilities, services);
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt16BigEndian(Version);
        Device.Write(ref writer);
        User.Write(ref writer);
        writer.UInt64BigEndian(RosterSeq);
        writer.UInt32BigEndian((uint)Capabilities);
        MessageFields.WriteStrings(ref writer, services, Limits.MaxHelloServices, Limits.MaxServiceNameLength);
    }

    /// <inheritdoc />
    public override string ToString() => $"Hello v{Version} device {Device} user {User} roster {RosterSeq}";
}
