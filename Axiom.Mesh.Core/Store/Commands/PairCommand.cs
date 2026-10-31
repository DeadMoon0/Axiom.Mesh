using System;

namespace Axiom.Mesh.Core;

/// <summary>Records a user as known through pairing (SPEC.md §1.2, §13): its Roster may be stored from then on.</summary>
internal sealed class PairCommand : StoreCommand<bool>
{
    /// <summary>A paired user.</summary>
    /// <param name="userKey">The user's key, as the pairing proved it.</param>
    /// <param name="name">The user's display name as the pairing showed it; empty when none was shown.</param>
    public PairCommand(PublicKey userKey, string name)
    {
        UserKey = userKey ?? throw new ArgumentNullException(nameof(userKey));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>The user's key.</summary>
    public PublicKey UserKey { get; }

    /// <summary>The user's display name (PAIR-SPEC.md §9.1); empty when none was shown.</summary>
    public string Name { get; }
}
