using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>An AMP/1 session with a peer (LOCAL-SPEC.md §5.8).</summary>
public sealed record Session : IWireRecord<Session>
{
    /// <summary>The peer device.</summary>
    public DeviceId DeviceId { get; init; }

    /// <summary>Its user.</summary>
    public UserId UserId { get; init; }

    /// <summary>Inbound (accepted) or outbound (dialled).</summary>
    public SessionDirection Direction { get; init; }

    /// <summary>The peer's address, <c>host:port</c>.</summary>
    public string Remote { get; init; } = "";

    /// <summary>This node's address for the session, <c>host:port</c>.</summary>
    public string Local { get; init; } = "";

    /// <summary>When the TCP connection was made.</summary>
    public ulong Since { get; init; }

    /// <summary>When something was last received.</summary>
    public ulong LastReceived { get; init; }

    /// <summary>The negotiated cipher suite.</summary>
    public string TlsSuite { get; init; } = "";

    /// <summary>Connected, Established or Switched.</summary>
    public SessionState State { get; init; }

    /// <summary>The service protocol while Switched; else empty.</summary>
    public string Service { get; init; } = "";

    /// <summary>Bytes received.</summary>
    public ulong BytesIn { get; init; }

    /// <summary>Bytes sent.</summary>
    public ulong BytesOut { get; init; }

    /// <summary>Objects received.</summary>
    public ulong ObjectsIn { get; init; }

    /// <summary>Objects sent.</summary>
    public ulong ObjectsOut { get; init; }

    /// <summary>Hashes this node has outstanding toward the peer (SPEC.md §7.5).</summary>
    public ushort WantsOut { get; init; }

    /// <summary>Hashes the peer has outstanding toward this node.</summary>
    public ushort WantsIn { get; init; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Session? value)
    {
        var read = new Session
        {
            DeviceId = DeviceId.Read(ref reader),
            UserId = UserId.Read(ref reader),
            Direction = (SessionDirection)reader.UInt8(),
            Remote = Wire.ReadString(ref reader, FieldBounds.AddressLength),
            Local = Wire.ReadString(ref reader, FieldBounds.AddressLength),
            Since = reader.UInt64BigEndian(),
            LastReceived = reader.UInt64BigEndian(),
            TlsSuite = Wire.ReadString(ref reader, FieldBounds.TlsSuiteLength),
            State = (SessionState)reader.UInt8(),
            Service = Wire.ReadString(ref reader, FieldBounds.ServiceProtocolLength),
            BytesIn = reader.UInt64BigEndian(),
            BytesOut = reader.UInt64BigEndian(),
            ObjectsIn = reader.UInt64BigEndian(),
            ObjectsOut = reader.UInt64BigEndian(),
            WantsOut = reader.UInt16BigEndian(),
            WantsIn = reader.UInt16BigEndian(),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        DeviceId.Write(ref writer);
        UserId.Write(ref writer);
        writer.UInt8((byte)Direction);
        Wire.WriteString(ref writer, Remote, FieldBounds.AddressLength);
        Wire.WriteString(ref writer, Local, FieldBounds.AddressLength);
        writer.UInt64BigEndian(Since);
        writer.UInt64BigEndian(LastReceived);
        Wire.WriteString(ref writer, TlsSuite, FieldBounds.TlsSuiteLength);
        writer.UInt8((byte)State);
        Wire.WriteString(ref writer, Service, FieldBounds.ServiceProtocolLength);
        writer.UInt64BigEndian(BytesIn);
        writer.UInt64BigEndian(BytesOut);
        writer.UInt64BigEndian(ObjectsIn);
        writer.UInt64BigEndian(ObjectsOut);
        writer.UInt16BigEndian(WantsOut);
        writer.UInt16BigEndian(WantsIn);
    }
}
