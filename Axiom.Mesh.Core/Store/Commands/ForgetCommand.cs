namespace Axiom.Mesh.Core;

/// <summary>
/// Forgets a paired user (LOCAL-SPEC.md §6.2 <c>ForgetUser</c>): the user is no longer known, its Roster and devices are
/// dropped from the index and its Roster's pin released, so its devices are refused from the next handshake and their
/// sessions closed. Completes with whether the user was held.
/// </summary>
internal sealed class ForgetCommand : StoreCommand<bool>
{
    /// <summary>A user to forget.</summary>
    /// <param name="user">The user; never the own one, and not an owner or grantee of a followed Package (the request checks).</param>
    public ForgetCommand(UserId user) => User = user;

    /// <summary>The user.</summary>
    public UserId User { get; }
}
