using System;

namespace Axiom.Mesh;

/// <summary>
/// The duplicate rule (SPEC.md §4.5): a node holds at most one session per device id, and when a second one becomes
/// established both nodes keep the same one without talking.
/// </summary>
public static class DuplicateSessions
{
    /// <summary>
    /// Decides which session to keep:
    /// <list type="number">
    /// <item>the old session received nothing for 60 seconds: it is dead, keep the new;</item>
    /// <item>both were dialled by the same device: keep the new;</item>
    /// <item>otherwise keep the session dialled by the device with the lower id.</item>
    /// </list>
    /// </summary>
    /// <param name="oldSilentFor">How long the old session has received nothing.</param>
    /// <param name="oldDialledBy">The device that dialled the old session.</param>
    /// <param name="newDialledBy">The device that dialled the new session.</param>
    /// <returns>The session to keep.</returns>
    public static KeptSession Decide(TimeSpan oldSilentFor, DeviceId oldDialledBy, DeviceId newDialledBy)
    {
        if (oldSilentFor >= TimeSpan.FromSeconds(Limits.DeadSessionSeconds))
            return KeptSession.New;
        if (oldDialledBy == newDialledBy)
            return KeptSession.New;
        return oldDialledBy < newDialledBy ? KeptSession.Old : KeptSession.New;
    }
}
