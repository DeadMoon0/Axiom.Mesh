using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// <c>HelloAck</c> (2, LOCAL-SPEC.md §4.2): the Host accepted the <see cref="Hello"/>; the session is Ready. It tells the
/// client what it may rely on for the connection's life: the granted role, the Host's flags, the device's identity
/// and where the store sequence stands, in which epoch.
/// </summary>
public sealed class HelloAck : LocalMessage, IHostMessage
{
    /// <summary>The protocol version, 1.</summary>
    public ushort Version { get; init; } = LocalProtocol.Version;

    /// <summary>For logs; never interpreted.</summary>
    public string HostVersion { get; init; } = "";

    /// <summary>The role granted; equals <see cref="Hello.Role"/>.</summary>
    public required Role Role { get; init; }

    /// <summary>The Host's flags for this connection (§14.4).</summary>
    public HostCapabilities Capabilities { get; init; }

    /// <summary>The device's identity; not present before <c>CreateIdentity</c> or enrolment.</summary>
    public required Identity Identity { get; init; }

    /// <summary>
    /// The store epoch (§1.2, §9.3): store sequences are comparable only within one epoch, so a client that kept an index
    /// under another epoch discards it (<see cref="IndexCatchUp"/>).
    /// </summary>
    public Hash32 StoreEpoch { get; init; }

    /// <summary>The store sequence of the newest stored Entry; 0 when none.</summary>
    public ulong StoreSeq { get; init; }

    /// <inheritdoc />
    public override ushort MessageId => LocalMessageIds.HelloAck;

    /// <inheritdoc />
    public override string ToString() => $"HelloAck(v{Version}, {Role}, {Capabilities}, epoch {StoreEpoch}, seq {StoreSeq})";

    internal static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out HelloAck? message)
    {
        var read = new HelloAck
        {
            Version = reader.UInt16BigEndian(),
            HostVersion = Wire.ReadString(ref reader, FieldBounds.VersionLength),
            Role = (Role)reader.UInt8(),
            Capabilities = (HostCapabilities)reader.UInt32BigEndian(),
            Identity = LocalWire.ReadRecord<Identity>(ref reader),
            StoreEpoch = Hash32.Read(ref reader),
            StoreSeq = reader.UInt64BigEndian(),
        };
        message = reader.Ok ? read : null;
        return message is not null;
    }

    internal override void WritePayload(ref WireWriter writer)
    {
        writer.UInt16BigEndian(Version);
        Wire.WriteString(ref writer, HostVersion, FieldBounds.VersionLength);
        writer.UInt8((byte)Role);
        writer.UInt32BigEndian((uint)Capabilities);
        Identity.Write(ref writer);
        StoreEpoch.Write(ref writer);
        writer.UInt64BigEndian(StoreSeq);
    }
}
