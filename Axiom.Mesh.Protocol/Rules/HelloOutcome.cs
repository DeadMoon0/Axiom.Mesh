namespace Axiom.Mesh.Protocol;

/// <summary>What <see cref="HelloRule.Check"/> decided: <see cref="Ok"/> or <see cref="Refused"/>. A closed set.</summary>
public abstract class HelloOutcome
{
    private HelloOutcome()
    {
    }

    /// <summary>The <c>Hello</c> passed every check of SPEC.md §7.1: the session is established.</summary>
    public sealed class Ok : HelloOutcome
    {
        internal Ok(TrustedDevice peer, RosterFollowUp followUp)
        {
            Peer = peer;
            FollowUp = followUp;
        }

        /// <summary>The peer's device as the node's trust knows it: its user, its key, not removed.</summary>
        public TrustedDevice Peer { get; }

        /// <summary>What to send about the peer's user's Roster, right after the own <c>Hello</c>.</summary>
        public RosterFollowUp FollowUp { get; }

        /// <inheritdoc />
        public override string ToString() => $"Ok {Peer.Id}, then {FollowUp}";
    }

    /// <summary>The <c>Hello</c> failed a check: a violation; close with <see cref="Bye"/> (§7.1, §12.1).</summary>
    public sealed class Refused : HelloOutcome
    {
        internal Refused(string detail) => Bye = new Bye(ByeReason.Unauthorized, detail);

        /// <summary>The closing message: <see cref="ByeReason.Unauthorized"/> with the failed check as its detail.</summary>
        public Bye Bye { get; }

        /// <inheritdoc />
        public override string ToString() => $"Refused: {Bye.Detail}";
    }
}
