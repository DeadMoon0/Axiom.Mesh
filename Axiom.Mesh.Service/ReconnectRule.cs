using System;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Service;

/// <summary>
/// What a service does when its connection could not be opened or ended (LOCAL-SPEC.md §2.3, §4.2, decision 27): it
/// reconnects with the token it has, waiting 1 second after the first failure and doubling up to 30; only a Host that
/// refused the name or its token, or speaks another version, makes it exit, with <see cref="ServiceExitCode.Refused"/>, so
/// that the supervisor does not restart it in a loop.
/// </summary>
public static class ReconnectRule
{
    /// <summary>Decides.</summary>
    /// <param name="refusal">
    /// The reason of the <c>Bye</c> the Host refused the <c>Hello</c> with; <see langword="null"/> when the connection could
    /// not be opened at all or ended after <c>Hello</c> was answered.
    /// </param>
    /// <param name="refusalDetail">The refusing <c>Bye</c>'s detail (<c>service</c>, <c>version</c>, <c>busy</c>); empty when none.</param>
    /// <param name="attempt">1 after the first failure in a row, 2 after the second, and so on.</param>
    /// <returns>Retry after <see cref="Reconnect.Delay"/>, or exit.</returns>
    public static ReconnectDecision Decide(ByeReason? refusal, string refusalDetail, int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        return refusal is ByeReason.Refused && refusalDetail is "service" or "version"
            ? ReconnectDecision.Exit(ServiceExitCode.Refused)
            : ReconnectDecision.Retry(Reconnect.Delay(attempt));
    }
}
