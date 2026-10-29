using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// What a Roster's bytes say (SPEC.md §8.8), well-formed by construction: the user id derives from the user key, every
/// device id from its key, the devices ascend by id without repeats, and none is the user's own key. It carries no
/// trust of its own; see <see cref="UnverifiedRoster"/> and <see cref="VerifiedRoster"/>.
/// </summary>
public sealed class RosterContent
{
    private readonly RosterDevice[] devices;

    private RosterContent(ObjectHash hash, PublicKey userKey, ulong seq, RosterDevice[] devices, Signature64 signature)
    {
        Hash = hash;
        UserKey = userKey;
        Seq = seq;
        this.devices = devices;
        Signature = signature;
    }

    /// <summary>The Roster's object hash.</summary>
    public ObjectHash Hash { get; }

    /// <summary>The user, <c>H(UserKey)</c>.</summary>
    public UserId User => UserKey.AsUserId();

    /// <summary>The user key, which signs the Roster.</summary>
    public PublicKey UserKey { get; }

    /// <summary>The sequence number; the highest valid one is current.</summary>
    public ulong Seq { get; }

    /// <summary>The devices, ascending by id.</summary>
    public IReadOnlyList<RosterDevice> Devices => devices;

    /// <summary>The user key's signature.</summary>
    public Signature64 Signature { get; }

    /// <summary>What decides whether this Roster is current (SPEC.md §8.8).</summary>
    public ObjectSeq Stamp => new(Seq, Hash);

    /// <summary>Finds a device by id.</summary>
    /// <param name="id">The device id.</param>
    /// <param name="device">The device, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the Roster lists it.</returns>
    public bool TryGetDevice(DeviceId id, [NotNullWhen(true)] out RosterDevice? device)
    {
        int low = 0;
        int high = devices.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int order = devices[middle].Id.CompareTo(id);
            if (order == 0)
            {
                device = devices[middle];
                return true;
            }

            if (order < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        device = null;
        return false;
    }

    internal static RosterContent? Parse(HashedObject source, ref WireReader reader, out int signedLength, out ObjectFault fault)
    {
        signedLength = 0;
        UserId user = UserId.Read(ref reader);
        PublicKey.TryRead(ref reader, out PublicKey? userKey);
        ulong seq = reader.UInt64BigEndian();
        RosterDevice[]? devices = ReadDevices(ref reader, user, out fault);
        if (devices is null)
            return null;

        signedLength = reader.Position;
        if (!ObjectReading.SignatureAtEnd(ref reader, "Roster", out Signature64 signature, out fault))
            return null;

        if (user != userKey!.AsUserId())
        {
            ObjectReading.Fail("A Roster's userId is not H(userKey) (SPEC.md §8.8).", out fault);
            return null;
        }

        return new RosterContent(source.Hash, userKey, seq, devices, signature);
    }

    private static RosterDevice[]? ReadDevices(ref WireReader reader, UserId user, out ObjectFault fault)
    {
        fault = default;
        int count = Wire.ReadListCount(ref reader, Limits.MaxRosterDevices);
        var devices = new RosterDevice[count];
        for (int i = 0; i < count && reader.Ok; i++)
        {
            DeviceId id = DeviceId.Read(ref reader);
            PublicKey.TryRead(ref reader, out PublicKey? key);
            string name = Wire.ReadString(ref reader, Limits.MaxDeviceNameLength);
            ulong addedAt = reader.UInt64BigEndian();
            bool removed = reader.Boolean();
            if (!reader.Ok)
                return devices;

            string? problem =
                id != key!.AsDeviceId() ? "A Roster device's id is not H(key) (SPEC.md §8.8)."
                : id.Value == user.Value ? "A Roster lists its own user key as a device; a key is a user key or a device key, never both (SPEC.md §2.3)."
                : i > 0 && devices[i - 1].Id >= id ? "A Roster's devices are not ascending by id, or an id repeats (SPEC.md §8.8)."
                : null;
            if (problem is not null)
            {
                ObjectReading.Fail(problem, out fault);
                return null;
            }

            devices[i] = new RosterDevice(key, name, addedAt, removed);
        }

        return devices;
    }
}
