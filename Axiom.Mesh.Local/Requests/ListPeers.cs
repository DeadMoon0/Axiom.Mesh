using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListPeers</c> (368, LOCAL-SPEC.md §6.6): Lists known peers, ascending by device id.</summary>
public sealed record ListPeers : LocalRequest, IRequest<ListPeers, IReadOnlyList<Peer>>
{
    private static readonly ListPeers Empty = new();

    /// <summary>The message id, 368 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 368;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListPeers";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.6).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out IReadOnlyList<Peer> ok)
    {
        ok = LocalWire.ReadList<Peer>(ref reader, FieldBounds.PeersCount);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, IReadOnlyList<Peer> ok)
    {
        LocalWire.WriteList(ref writer, ok, FieldBounds.PeersCount);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListPeers? value)
    {
        value = Empty;
        return true;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
    }
}
