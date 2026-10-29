using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>This device's identity (LOCAL-SPEC.md §5.1).</summary>
public sealed record Identity : IWireRecord<Identity>
{
    /// <summary>Whether the device has an identity; when <see langword="false"/> every other field is zero or empty.</summary>
    public bool Present { get; init; }

    /// <summary>The own user.</summary>
    public UserId UserId { get; init; }

    /// <summary>The user's display name, local to this device's database.</summary>
    public string UserName { get; init; } = "";

    /// <summary>Whether the user's private key is on this device.</summary>
    public bool UserKeyPresent { get; init; }

    /// <summary>This device.</summary>
    public DeviceId DeviceId { get; init; }

    /// <summary>This device's name in the current Roster.</summary>
    public string DeviceName { get; init; } = "";

    /// <summary>The sequence number of the own user's current Roster.</summary>
    public ulong RosterSeq { get; init; }

    /// <summary>When the identity was created or enrolled here, in milliseconds since the Unix epoch.</summary>
    public ulong Created { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Identity? value)
    {
        var read = new Identity
        {
            Present = reader.Boolean(),
            UserId = UserId.Read(ref reader),
            UserName = Wire.ReadString(ref reader, FieldBounds.UserNameLength),
            UserKeyPresent = reader.Boolean(),
            DeviceId = DeviceId.Read(ref reader),
            DeviceName = Wire.ReadString(ref reader, FieldBounds.DeviceNameLength),
            RosterSeq = reader.UInt64BigEndian(),
            Created = reader.UInt64BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        writer.Boolean(Present);
        UserId.Write(ref writer);
        Wire.WriteString(ref writer, UserName, FieldBounds.UserNameLength);
        writer.Boolean(UserKeyPresent);
        DeviceId.Write(ref writer);
        Wire.WriteString(ref writer, DeviceName, FieldBounds.DeviceNameLength);
        writer.UInt64BigEndian(RosterSeq);
        writer.UInt64BigEndian(Created);
    }
}
