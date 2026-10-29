using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>The current or the last pairing (LOCAL-SPEC.md §5.15, §6.2).</summary>
public sealed record Pairing : IWireRecord<Pairing>
{
    /// <summary>The pairing, never 0.</summary>
    public uint PairingId { get; init; }

    /// <summary>This side's mode.</summary>
    public PairingMode Mode { get; init; }

    /// <summary>Where it stands.</summary>
    public PairingState State { get; init; }

    /// <summary>What this side shows; empty unless this side shows. Opaque: show it as it is.</summary>
    public string Code { get; init; } = "";

    /// <summary>The short authentication string once both sides are connected; else empty.</summary>
    public string Sas { get; init; } = "";

    /// <summary>The other side's user; all zero until known.</summary>
    public UserId RemoteUserId { get; init; }

    /// <summary>The other side's display name; empty until known.</summary>
    public string RemoteName { get; init; } = "";

    /// <summary>The other side's device; all zero until known.</summary>
    public DeviceId RemoteDeviceId { get; init; }

    /// <summary>When it expires, 10 minutes after it started.</summary>
    public ulong Expires { get; init; }

    /// <summary>Why it failed, when Failed; else empty.</summary>
    public string Failure { get; init; } = "";

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Pairing? value)
    {
        var read = new Pairing
        {
            PairingId = reader.UInt32BigEndian(),
            Mode = (PairingMode)reader.UInt8(),
            State = (PairingState)reader.UInt8(),
            Code = Wire.ReadString(ref reader, FieldBounds.PairingCodeLength),
            Sas = Wire.ReadString(ref reader, FieldBounds.SasLength),
            RemoteUserId = UserId.Read(ref reader),
            RemoteName = Wire.ReadString(ref reader, FieldBounds.UserNameLength),
            RemoteDeviceId = DeviceId.Read(ref reader),
            Expires = reader.UInt64BigEndian(),
            Failure = Wire.ReadString(ref reader, FieldBounds.FailureLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        writer.UInt32BigEndian(PairingId);
        writer.UInt8((byte)Mode);
        writer.UInt8((byte)State);
        Wire.WriteString(ref writer, Code, FieldBounds.PairingCodeLength);
        Wire.WriteString(ref writer, Sas, FieldBounds.SasLength);
        RemoteUserId.Write(ref writer);
        Wire.WriteString(ref writer, RemoteName, FieldBounds.UserNameLength);
        RemoteDeviceId.Write(ref writer);
        writer.UInt64BigEndian(Expires);
        Wire.WriteString(ref writer, Failure, FieldBounds.FailureLength);
    }
}
