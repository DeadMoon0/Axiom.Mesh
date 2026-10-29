using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// A Roster to sign (SPEC.md §8.8). The user id is the signer's key's, every device id its key's, and the devices are
/// sorted for the caller.
/// </summary>
public sealed class RosterDraft
{
    /// <summary>The sequence number, higher than every earlier Roster of the user.</summary>
    public required ulong Seq { get; init; }

    /// <summary>The devices, in any order; at most 256, each listed once, none with the user's own key.</summary>
    public required IReadOnlyList<RosterDevice> Devices { get; init; }

    /// <summary>Encodes and signs the Roster with the user key.</summary>
    /// <param name="user">The user's signer.</param>
    /// <returns>The Roster, verified.</returns>
    /// <exception cref="ArgumentException">A device repeats or is the user key, or a field breaks its bound.</exception>
    public VerifiedRoster Sign(ISigner user)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(Devices);
        UserId userId = user.PublicKey.AsUserId();
        RosterDevice[] devices = [.. Devices.OrderBy(d => d.Id)];
        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].Id.Value == userId.Value)
                throw new ArgumentException("The user key is listed as a device; a key is a user key or a device key, never both (SPEC.md §2.3).", nameof(Devices));
            if (i > 0 && devices[i - 1].Id == devices[i].Id)
                throw new ArgumentException($"Device {devices[i].Id} is listed twice (SPEC.md §8.8).", nameof(Devices));
        }

        ArrayBufferWriter<byte> output = ObjectEncoding.Start(ObjectTag.Roster);
        var writer = new WireWriter(output);
        userId.Write(ref writer);
        user.PublicKey.Write(ref writer);
        writer.UInt64BigEndian(Seq);
        Wire.WriteListCount(ref writer, devices.Length, Limits.MaxRosterDevices);
        foreach (RosterDevice device in devices)
        {
            device.Id.Write(ref writer);
            device.Key.Write(ref writer);
            Wire.WriteString(ref writer, device.Name, Limits.MaxDeviceNameLength);
            writer.UInt64BigEndian(device.AddedAt);
            writer.Boolean(device.Removed);
        }

        UnverifiedRoster roster = ObjectEncoding.ParseOwn<UnverifiedRoster>(ObjectEncoding.Sign(output, SigningDomain.Roster, user));
        return VerifiedRoster.TryVerify(roster, out VerifiedRoster? verified)
            ? verified
            : throw ObjectEncoding.SignerDefect("Roster");
    }
}
