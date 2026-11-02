using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// <c>StartPairingByDevice</c> (278, LOCAL-SPEC.md §6.2): Starts a pairing on the entering side without a code: the other
/// side is named by the device id and the addresses discovery announced for it (<c>ListDiscovered</c>, §6.6; PAIR-SPEC.md
/// §2.4). One pairing at a time per Host (<c>Busy</c>).
/// </summary>
public sealed record StartPairingByDevice : LocalRequest, IRequest<StartPairingByDevice, Pairing>
{
    /// <summary>The message id, 278 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 278;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "StartPairingByDevice";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.2).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>Which side this is: an entering mode, <c>JoinUser</c> or <c>AddDevice</c> (§14.10).</summary>
    public required PairingMode Mode { get; init; }

    /// <summary>The other side's device id as discovery announced it; never all zero.</summary>
    public DeviceId Device { get; init; }

    /// <summary>Where it listens, <c>host:port</c> or <c>[ipv6]:port</c>: 1 to 8 addresses in the order to try.</summary>
    public IReadOnlyList<string> Addresses { get; init; } = [];

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out Pairing ok)
    {
        bool read = Pairing.TryRead(ref reader, out Pairing? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, Pairing ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out StartPairingByDevice? value)
    {
        var read = new StartPairingByDevice
        {
            Mode = (PairingMode)reader.UInt8(),
            Device = DeviceId.Read(ref reader),
            Addresses = LocalWire.ReadStrings(ref reader, FieldBounds.AddressLength, FieldBounds.AddressesCount),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt8((byte)Mode);
        Device.Write(ref writer);
        LocalWire.WriteStrings(ref writer, Addresses, FieldBounds.AddressLength, FieldBounds.AddressesCount);
    }
}
