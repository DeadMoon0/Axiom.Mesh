using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>StartPairing</c> (272, LOCAL-SPEC.md §6.2): Starts a pairing or an enrolment; one at a time per Host (<c>Busy</c>).</summary>
public sealed record StartPairing : LocalRequest, IRequest<StartPairing, Pairing>
{
    /// <summary>The message id, 272 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 272;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "StartPairing";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.2).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>Which side this is (§14.10).</summary>
    public required PairingMode Mode { get; init; }

    /// <summary>Modes JoinUser and AddDevice: the code the other side showed; else empty.</summary>
    public string Code { get; init; } = "";

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out StartPairing? value)
    {
        var read = new StartPairing
        {
            Mode = (PairingMode)reader.UInt8(),
            Code = Wire.ReadString(ref reader, FieldBounds.PairingCodeLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt8((byte)Mode);
        Wire.WriteString(ref writer, Code, FieldBounds.PairingCodeLength);
    }
}
