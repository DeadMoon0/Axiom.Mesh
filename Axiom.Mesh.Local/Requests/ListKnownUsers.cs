using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListKnownUsers</c> (276, LOCAL-SPEC.md §6.2): Lists the known users, the own user included, ascending by id.</summary>
public sealed record ListKnownUsers : LocalRequest, IRequest<ListKnownUsers, IReadOnlyList<User>>
{
    private static readonly ListKnownUsers Empty = new();

    /// <summary>The message id, 276 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 276;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListKnownUsers";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.2).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out IReadOnlyList<User> ok)
    {
        ok = LocalWire.ReadList<User>(ref reader, FieldBounds.KnownUsersCount);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, IReadOnlyList<User> ok)
    {
        LocalWire.WriteList(ref writer, ok, FieldBounds.KnownUsersCount);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListKnownUsers? value)
    {
        value = Empty;
        return true;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
    }
}
