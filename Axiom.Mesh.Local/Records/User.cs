using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>A known user (LOCAL-SPEC.md §5.2).</summary>
public sealed record User : IWireRecord<User>
{
    /// <summary>The user.</summary>
    public UserId UserId { get; init; }

    /// <summary>The display name, local to this device's database.</summary>
    public string Name { get; init; } = "";

    /// <summary>Whether this is the own user.</summary>
    public bool Self { get; init; }

    /// <summary>Since when the user is known, in milliseconds since the Unix epoch.</summary>
    public ulong KnownSince { get; init; }

    /// <summary>The current Roster held for the user; 0 when none.</summary>
    public ulong RosterSeq { get; init; }

    /// <summary>Devices in that Roster that are not removed.</summary>
    public ushort Devices { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out User? value)
    {
        var read = new User
        {
            UserId = UserId.Read(ref reader),
            Name = Wire.ReadString(ref reader, FieldBounds.UserNameLength),
            Self = reader.Boolean(),
            KnownSince = reader.UInt64BigEndian(),
            RosterSeq = reader.UInt64BigEndian(),
            Devices = reader.UInt16BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        UserId.Write(ref writer);
        Wire.WriteString(ref writer, Name, FieldBounds.UserNameLength);
        writer.Boolean(Self);
        writer.UInt64BigEndian(KnownSince);
        writer.UInt64BigEndian(RosterSeq);
        writer.UInt16BigEndian(Devices);
    }
}
