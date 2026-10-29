using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>RemoveDevice</c> (289, LOCAL-SPEC.md §6.3): Removes a device of the own user permanently in a new Roster; a user-key request.</summary>
public sealed record RemoveDevice : LocalRequest, IRequest<RemoveDevice, Device>
{
    /// <summary>The message id, 289 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 289;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "RemoveDevice";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.3).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The device.</summary>
    public required DeviceId DeviceId { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out Device ok)
    {
        bool read = Device.TryRead(ref reader, out Device? value);
        ok = value!;
        return read;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, Device ok)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ok.Write(ref writer);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out RemoveDevice? value)
    {
        var read = new RemoveDevice
        {
            DeviceId = DeviceId.Read(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        DeviceId.Write(ref writer);
    }
}
