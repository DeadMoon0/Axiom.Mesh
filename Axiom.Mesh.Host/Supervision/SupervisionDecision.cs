using System;

namespace Axiom.Mesh.Host;

/// <summary>What <see cref="SupervisionRule"/> decided, the state to remember, and why, for the log.</summary>
/// <param name="Action">Restart, give up, or stopped.</param>
/// <param name="Delay">How long to wait before a restart; zero otherwise.</param>
/// <param name="Next">The state after this exit.</param>
/// <param name="Why">One line for the log.</param>
internal sealed record SupervisionDecision(SupervisionAction Action, TimeSpan Delay, SupervisionState Next, string Why);
