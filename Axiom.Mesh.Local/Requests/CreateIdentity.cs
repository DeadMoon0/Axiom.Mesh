using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>CreateIdentity</c> (257, LOCAL-SPEC.md §6.1): Creates the user key, a Roster with seq 1 holding this device, and the device certificate; <c>AlreadyExists</c> when an identity is present.</summary>
public sealed record CreateIdentity : LocalRequest, IRequest<CreateIdentity, Identity>
{
    /// <summary>The message id, 257 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 257;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "CreateIdentity";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.1).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The user's display name; MUST NOT be empty.</summary>
    public required string UserName { get; init; }

    /// <summary>This device's name; MUST NOT be empty.</summary>
    public required string DeviceName { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out Identity ok)
    {
        bool read = Identity.TryRead(ref reader, out Identity? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, Identity ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out CreateIdentity? value)
    {
        var read = new CreateIdentity
        {
            UserName = Wire.ReadString(ref reader, FieldBounds.UserNameLength),
            DeviceName = Wire.ReadString(ref reader, FieldBounds.DeviceNameLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        Wire.WriteString(ref writer, UserName, FieldBounds.UserNameLength);
        Wire.WriteString(ref writer, DeviceName, FieldBounds.DeviceNameLength);
    }
}
