using System;

namespace Axiom.Mesh.Core;

/// <summary>
/// A device's violations within the current hour (SPEC.md §12.1): the window opens at the first violation and closes
/// an hour later; a violation after it opens a new one. An immutable value.
/// </summary>
/// <param name="Count">Violations in the window.</param>
/// <param name="Since">When the window opened.</param>
internal readonly record struct ViolationWindow(int Count, DateTimeOffset Since)
{
    private static readonly TimeSpan Length = TimeSpan.FromSeconds(Limits.ViolationWindowSeconds);

    /// <summary>Counts one more violation at <paramref name="now"/>.</summary>
    /// <param name="now">When it happened.</param>
    /// <returns>The window with it.</returns>
    public ViolationWindow Add(DateTimeOffset now) =>
        Count == 0 || now - Since >= Length ? new ViolationWindow(1, now) : new ViolationWindow(Count + 1, Since);
}
