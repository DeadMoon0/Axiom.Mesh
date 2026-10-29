using System;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// What a node knows about the peer of a session apart from its <c>Hello</c>, as <see cref="HelloRule.Check"/> needs it.
/// </summary>
/// <param name="TlsKey">The public key of the peer's TLS certificate, which the handshake proved (SPEC.md §4.3).</param>
/// <param name="HeldRosterSeq">
/// The <c>seq</c> of the current Roster this node holds for the user whose Roster lists <paramref name="TlsKey"/>, read
/// from the same trust snapshot the rule is given; 0 when it holds none.
/// </param>
[ClosedShape("The two facts §7.1 compares a Hello with; another check is a new rule, not a new fact.")]
public readonly record struct PeerIdentityFacts(PublicKey TlsKey, ulong HeldRosterSeq)
{
    /// <summary>The public key of the peer's TLS certificate.</summary>
    public PublicKey TlsKey { get; } = TlsKey ?? throw new ArgumentNullException(nameof(TlsKey));
}
