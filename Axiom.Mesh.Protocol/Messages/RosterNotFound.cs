using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// <c>RosterNotFound</c> (16, SPEC.md §7.9), Established only: the answer to a <see cref="RosterQuery"/> for a Roster
/// the sender does not hold or may not serve (§9.8).
/// </summary>
public sealed class RosterNotFound : MeshMessage, IWireMessage<RosterNotFound>
{
    /// <summary>The answer for one user.</summary>
    /// <param name="user">The user that was asked for.</param>
    public RosterNotFound(UserId user) => User = user;

    /// <inheritdoc />
    public override ushort Id => MessageIds.RosterNotFound;

    /// <summary>The user that was asked for.</summary>
    public UserId User { get; }

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, out RosterNotFound message)
    {
        message = new RosterNotFound(UserId.Read(ref reader));
        return reader.Ok;
    }

    /// <inheritdoc />
    public override void Write(ref WireWriter writer) => User.Write(ref writer);

    /// <inheritdoc />
    public override string ToString() => $"RosterNotFound {User}";
}
