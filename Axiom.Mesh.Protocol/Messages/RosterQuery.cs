using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>RosterQuery</c> (9, SPEC.md §7.9), Established only: asks for the current Roster the peer holds for a user; the
/// answer is an <c>Object</c> carrying it or <see cref="RosterNotFound"/>, subject to §9.8.
/// </summary>
public sealed class RosterQuery : MeshMessage, IWireMessage<RosterQuery>
{
    /// <summary>A query for one user's Roster.</summary>
    /// <param name="user">The user.</param>
    public RosterQuery(UserId user) => User = user;

    /// <inheritdoc />
    public override ushort Id => MessageIds.RosterQuery;

    /// <summary>The user whose Roster is asked for.</summary>
    public UserId User { get; }

    /// <summary>The answer when the Roster is not held or may not be served (§9.8).</summary>
    /// <returns>A <see cref="RosterNotFound"/> for the same user.</returns>
    public RosterNotFound NotFound() => new(User);

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out RosterQuery message)
    {
        message = new RosterQuery(UserId.Read(ref reader));
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer) => User.Write(ref writer);

    /// <inheritdoc />
    public override string ToString() => $"RosterQuery {User}";
}
