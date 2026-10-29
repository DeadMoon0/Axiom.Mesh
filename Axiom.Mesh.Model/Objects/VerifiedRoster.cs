using System;
using System.Diagnostics.CodeAnalysis;

namespace Axiom.Mesh;

/// <summary>
/// A Roster whose signature verifies against its own user key (SPEC.md §8.8): storable for a known user, and for no
/// other. Which devices a node then treats as removed is <see cref="RosterRules"/>' decision, not the flags alone.
/// </summary>
public sealed class VerifiedRoster : StorableObject
{
    private VerifiedRoster(UnverifiedRoster roster)
        : base(roster.Hash, roster.Bytes) => Content = roster.Content;

    /// <summary>What the bytes say; signed by the user key it names.</summary>
    public RosterContent Content { get; }

    /// <inheritdoc />
    public override ObjectTag Tag => ObjectTag.Roster;

    /// <summary>
    /// Checks the Roster's signature over the bytes received against its own <c>userKey</c>, with domain
    /// <c>axiom-mesh/roster/1</c> (SPEC.md §2.2, §8.8).
    /// </summary>
    /// <param name="roster">The parsed Roster.</param>
    /// <param name="verified">The verified Roster, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the signature does not verify: the Roster is invalid.</returns>
    public static bool TryVerify(UnverifiedRoster roster, [NotNullWhen(true)] out VerifiedRoster? verified)
    {
        ArgumentNullException.ThrowIfNull(roster);
        bool valid = roster.Content.UserKey.Verify(Digests.Of(SigningDomain.Roster, roster.SignedBytes), roster.Content.Signature);
        verified = valid ? new VerifiedRoster(roster) : null;
        return valid;
    }
}
