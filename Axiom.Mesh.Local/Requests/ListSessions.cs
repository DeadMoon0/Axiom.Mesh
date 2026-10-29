using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListSessions</c> (369, LOCAL-SPEC.md §6.6): Lists AMP/1 sessions, ascending by device id.</summary>
public sealed record ListSessions : LocalRequest, IRequest<ListSessions, IReadOnlyList<Session>>
{
    private static readonly ListSessions Empty = new();

    /// <summary>The message id, 369 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 369;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListSessions";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.6).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out IReadOnlyList<Session> ok)
    {
        ok = LocalWire.ReadList<Session>(ref reader, FieldBounds.SessionsCount);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, IReadOnlyList<Session> ok)
    {
        LocalWire.WriteList(ref writer, ok, FieldBounds.SessionsCount);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListSessions? value)
    {
        value = Empty;
        return true;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
    }
}
