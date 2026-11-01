using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Host;

/// <summary>
/// What the supervisor of one spawned service remembers between its children: whether the daemon is stopping, and when the
/// recent crashes were (a crash is an exit within 10 seconds of the start).
/// </summary>
/// <param name="Stopping">The daemon stops: no child is started again.</param>
/// <param name="Crashes">When the crashes since the last long run happened, oldest first.</param>
internal sealed record SupervisionState(bool Stopping, IReadOnlyList<DateTimeOffset> Crashes)
{
    /// <summary>A supervisor that has seen nothing yet.</summary>
    public static SupervisionState Fresh { get; } = new(false, []);
}
