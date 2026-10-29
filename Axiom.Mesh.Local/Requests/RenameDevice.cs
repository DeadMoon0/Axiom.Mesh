using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>RenameDevice</c> (258, LOCAL-SPEC.md §6.1): Renames a device of the own user in a new Roster; needs the user key.</summary>
public sealed record RenameDevice : LocalRequest, IRequest<RenameDevice, Device>
{
    /// <summary>The message id, 258 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 258;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "RenameDevice";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.1).</summary>
    public static Roles AllowedRoles => Roles.User;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>A device of the own user, not removed.</summary>
    public required DeviceId DeviceId { get; init; }

    /// <summary>The new name; MUST NOT be empty.</summary>
    public required string Name { get; init; }

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
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out RenameDevice? value)
    {
        var read = new RenameDevice
        {
            DeviceId = DeviceId.Read(ref reader),
            Name = Wire.ReadString(ref reader, FieldBounds.DeviceNameLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        DeviceId.Write(ref writer);
        Wire.WriteString(ref writer, Name, FieldBounds.DeviceNameLength);
    }
}
