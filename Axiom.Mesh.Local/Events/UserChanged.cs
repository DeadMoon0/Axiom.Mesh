using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>UserChanged</c> (1032, LOCAL-SPEC.md §7.3): A user became known, a new current Roster is held for a known user, or a user was forgotten.</summary>
public sealed record UserChanged : LocalEvent, IEvent<UserChanged>
{
    /// <summary>The message id, 1032 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 1032;

    /// <summary>The topic whose subscriptions receive it (LOCAL-SPEC.md §7.2).</summary>
    public static Topic Topic => Topic.Users;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>What happened.</summary>
    public UserChange Change { get; init; }

    /// <summary>The user.</summary>
    public required User User { get; init; }

    /// <summary>The current Roster's devices, ascending by id; empty for Forgotten.</summary>
    public IReadOnlyList<Device> Devices { get; init; } = [];

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out UserChanged? value)
    {
        var read = new UserChanged
        {
            Change = (UserChange)reader.UInt8(),
            User = LocalWire.ReadRecord<User>(ref reader),
            Devices = LocalWire.ReadList<Device>(ref reader, FieldBounds.DevicesCount),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        writer.UInt8((byte)Change);
        User.Write(ref writer);
        LocalWire.WriteList(ref writer, Devices, FieldBounds.DevicesCount);
    }
}
