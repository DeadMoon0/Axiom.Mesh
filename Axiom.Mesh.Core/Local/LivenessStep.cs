using System;

namespace Axiom.Mesh.Core;

/// <summary>What <see cref="LivenessRule"/> decided: send a <c>Ping</c>, close as unresponsive, or wait.</summary>
/// <param name="Action">What to do.</param>
/// <param name="Wait">When to look again, for <see cref="LivenessAction.Wait"/>.</param>
internal readonly record struct LivenessStep(LivenessAction Action, TimeSpan Wait)
{
    /// <summary>Send a Ping now.</summary>
    public static LivenessStep Ping => new(LivenessAction.Ping, TimeSpan.Zero);

    /// <summary>Close with <c>Bye(Unresponsive)</c>.</summary>
    public static LivenessStep Unresponsive => new(LivenessAction.Unresponsive, TimeSpan.Zero);

    /// <summary>Look again later.</summary>
    /// <param name="wait">How much later.</param>
    /// <returns>The step.</returns>
    public static LivenessStep WaitFor(TimeSpan wait) => new(LivenessAction.Wait, wait);
}
