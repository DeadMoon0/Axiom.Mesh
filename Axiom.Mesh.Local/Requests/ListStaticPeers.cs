using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary><c>ListStaticPeers</c> (371, LOCAL-SPEC.md §6.6): Lists the static peers, ascending by address.</summary>
public sealed record ListStaticPeers : LocalRequest, IRequest<ListStaticPeers, IReadOnlyList<StaticPeer>>
{
    private static readonly ListStaticPeers Empty = new();

    /// <summary>The message id, 371 (LOCAL-SPEC.md §14.1).</summary>
    public static ushort Id => 371;

    /// <summary>The request's name in LOCAL-SPEC.md §14.1.</summary>
    public static string RequestName => "ListStaticPeers";

    /// <summary>The roles that may send it (LOCAL-SPEC.md §6.6).</summary>
    public static Roles AllowedRoles => Roles.User | Roles.Service;

    /// <inheritdoc />
    public override ushort MessageId => Id;

    /// <inheritdoc />
    public static bool TryReadOk(ref WireReader reader, [MaybeNullWhen(false)] out IReadOnlyList<StaticPeer> ok)
    {
        ok = LocalWire.ReadList<StaticPeer>(ref reader, FieldBounds.StaticPeersCount);
        return reader.Ok;
    }

    /// <inheritdoc />
    public static void WriteOk(ref WireWriter writer, IReadOnlyList<StaticPeer> ok)
    {
        LocalWire.WriteList(ref writer, ok, FieldBounds.StaticPeersCount);
    }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out ListStaticPeers? value)
    {
        value = Empty;
        return true;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer)
    {
    }
}
