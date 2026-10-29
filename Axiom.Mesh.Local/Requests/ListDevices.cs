using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListDevices</c> (288, LOCAL-SPEC.md §6.3): Lists a user's devices, removed ones included, ascending by id.</summary>
public sealed record ListDevices : LocalRequest, IRequest<ListDevices, IReadOnlyList<Device>>
{
    /// <summary>The message id, 288 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 288;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListDevices";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.3).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <summary>The user; all zero for the own user.</summary>
    public UserId UserId { get; init; }

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out IReadOnlyList<Device> ok)
    {
        ok = LocalWire.ReadList<Device>(ref reader, FieldBounds.DevicesCount);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, IReadOnlyList<Device> ok)
    {
        LocalWire.WriteList(ref writer, ok, FieldBounds.DevicesCount);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListDevices? value)
    {
        var read = new ListDevices
        {
            UserId = UserId.Read(ref reader),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
        UserId.Write(ref writer);
    }
}
