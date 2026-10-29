using System;

namespace Axiom.Mesh.Core;

/// <summary>
/// Liveness on the local port (LOCAL-SPEC.md §4.4): a <c>Ping</c> after 60 seconds without receiving anything, and
/// <c>Bye(Unresponsive)</c> when its <c>Pong</c> did not come within 30 seconds. Responsiveness, not activity: a client
/// that answers is never closed for being quiet (§1.6 decision 11).
/// </summary>
internal static class LivenessRule
{
    private static readonly TimeSpan PingAfter = TimeSpan.FromSeconds(Limits.LocalPingAfterSeconds);
    private static readonly TimeSpan PongWithin = TimeSpan.FromSeconds(Limits.LocalPongSeconds);

    /// <summary>Decides what to do now.</summary>
    /// <param name="silentFor">How long nothing was received from the client.</param>
    /// <param name="pingAge">How long ago the unanswered <c>Ping</c> was sent; <see langword="null"/> when none is outstanding.</param>
    /// <returns>Send a Ping, close, or look again after <see cref="LivenessStep.Wait"/>.</returns>
    public static LivenessStep Decide(TimeSpan silentFor, TimeSpan? pingAge)
    {
        if (pingAge is { } age)
            return age >= PongWithin ? LivenessStep.Unresponsive : LivenessStep.WaitFor(PongWithin - age);
        return silentFor >= PingAfter ? LivenessStep.Ping : LivenessStep.WaitFor(PingAfter - silentFor);
    }
}
