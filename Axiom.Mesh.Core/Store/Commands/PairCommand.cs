using System;

namespace Axiom.Mesh.Core;

/// <summary>Records a user as known through pairing (SPEC.md §1.2, §13): its Roster may be stored from then on.</summary>
internal sealed class PairCommand : StoreCommand<bool>
{
    /// <summary>A paired user.</summary>
    /// <param name="userKey">The user's key, as the pairing proved it.</param>
    public PairCommand(PublicKey userKey) => UserKey = userKey ?? throw new ArgumentNullException(nameof(userKey));

    /// <summary>The user's key.</summary>
    public PublicKey UserKey { get; }
}
