using System.Collections.Generic;

namespace Axiom.Mesh.Cli;

/// <summary>What running an <see cref="AutostartPlan"/> did.</summary>
/// <param name="Plan">The plan.</param>
/// <param name="Steps">Each step and how it ended, in order.</param>
/// <param name="Removed">For an uninstall: whether a registration existed and was removed.</param>
/// <param name="Stopped">How many running daemons were stopped.</param>
internal sealed record AutostartOutcome(AutostartPlan Plan, IReadOnlyList<StepOutcome> Steps, bool Removed, int Stopped);
