using System;

namespace Axiom.Mesh.Local;

/// <summary>
/// How late a service may still send a <c>Reply</c> (LOCAL-SPEC.md §3.6, §9.8): the Host answers its caller at the 60-second
/// deadline and ignores a later reply for 5 minutes, after which a reply naming the forgotten id is a violation. A service
/// therefore sends nothing more than 120 seconds after its callback arrived, well inside the 5 minutes.
/// </summary>
public static class CallbackReplyRule
{
    /// <summary>The latest a reply is sent after its callback arrived.</summary>
    public static TimeSpan Latest { get; } = TimeSpan.FromSeconds(120);

    /// <summary>Whether a reply may still be sent.</summary>
    /// <param name="sinceReceived">How long ago its callback arrived.</param>
    /// <returns><see langword="true"/> up to and including 120 seconds.</returns>
    public static bool MaySend(TimeSpan sinceReceived) => sinceReceived <= Latest;
}
