using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// What became of one callback toward a service (LOCAL-SPEC.md §3.6, §9.8): its <c>Reply</c> when the service answered in
/// time, else why it ended without one. Only the three factories make one, so a reply is present exactly when the
/// callback was answered. The reply's body is detached: it outlives the read that brought it.
/// </summary>
internal sealed class CallbackOutcome
{
    private CallbackOutcome(CallbackEnd end, Reply? reply)
    {
        End = end;
        Reply = reply;
    }

    /// <summary>The deadline passed first.</summary>
    public static CallbackOutcome Expired { get; } = new(CallbackEnd.Expired, null);

    /// <summary>The service's connection ended first.</summary>
    public static CallbackOutcome ConnectionEnded { get; } = new(CallbackEnd.ConnectionEnded, null);

    /// <summary>How it ended.</summary>
    public CallbackEnd End { get; }

    /// <summary>The service's reply; present exactly when <see cref="End"/> is <see cref="CallbackEnd.Replied"/>.</summary>
    public Reply? Reply { get; }

    /// <summary>The service answered in time.</summary>
    /// <param name="reply">Its reply, detached from the read buffer.</param>
    /// <returns>The outcome.</returns>
    public static CallbackOutcome Replied(Reply reply) => new(CallbackEnd.Replied, reply ?? throw new System.ArgumentNullException(nameof(reply)));

    /// <inheritdoc />
    public override string ToString() => Reply is { } reply ? $"{End} {reply}" : End.ToString();
}
