using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// <c>Hello</c> (1, LOCAL-SPEC.md §4.2): the first frame of every connection, sent by the client within 10 seconds of
/// connecting. The Host answers <see cref="HelloAck"/> or <see cref="Bye"/>.
/// </summary>
public sealed class Hello : LocalMessage, IClientMessage
{
    /// <summary>The protocol version, 1.</summary>
    public ushort Version { get; init; } = LocalProtocol.Version;

    /// <summary>The role asked for; a <c>Hello</c> with any role but user or service is a violation (§11.3).</summary>
    public required Role Role { get; init; }

    /// <summary>The service's name in the service role (at most 32 bytes); the client's name, for logs, in the user role.</summary>
    public required string Name { get; init; }

    /// <summary>For logs; never interpreted.</summary>
    public string ClientVersion { get; init; } = "";

    /// <summary>The client's flags (§14.4).</summary>
    public ClientCapabilities Capabilities { get; init; }

    /// <summary>A spawned or external service's token; <see cref="ServiceToken.None"/> otherwise.</summary>
    public ServiceToken Token { get; init; }

    /// <inheritdoc />
    public override ushort MessageId => LocalMessageIds.Hello;

    /// <inheritdoc />
    public override string ToString() => $"Hello(v{Version}, {Role}, \"{Name}\", {Token})";

    internal static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out Hello? message)
    {
        var read = new Hello
        {
            Version = reader.UInt16BigEndian(),
            Role = (Role)reader.UInt8(),
            Name = Wire.ReadString(ref reader, FieldBounds.NameLength),
            ClientVersion = Wire.ReadString(ref reader, FieldBounds.VersionLength),
            Capabilities = (ClientCapabilities)reader.UInt32BigEndian(),
            Token = ServiceToken.Read(ref reader),
        };

        // A role outside the registry is a violation, as a field out of its bounds is (§4.2 check 2, §11.3).
        if (read.Role is not (Role.User or Role.Service))
            reader.Fail();
        message = reader.Ok ? read : null;
        return message is not null;
    }

    internal override void WritePayload(ref WireWriter writer)
    {
        if (Role is not (Role.User or Role.Service))
            throw new ArgumentException($"A Hello asks for the user or the service role, not {Role} (LOCAL-SPEC.md §4.2).", nameof(Role));

        writer.UInt16BigEndian(Version);
        writer.UInt8((byte)Role);
        Wire.WriteString(ref writer, Name, FieldBounds.NameLength);
        Wire.WriteString(ref writer, ClientVersion, FieldBounds.VersionLength);
        writer.UInt32BigEndian((uint)Capabilities);
        Token.Write(ref writer);
    }
}
