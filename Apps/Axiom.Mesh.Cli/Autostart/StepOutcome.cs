namespace Axiom.Mesh.Cli;

/// <summary>How one autostart step ended.</summary>
/// <param name="Step">The step.</param>
/// <param name="ExitCode">The program's exit code for a run step; 0 for the others.</param>
/// <param name="Output">What the program printed, or what a file step found.</param>
internal sealed record StepOutcome(AutostartStep Step, int ExitCode, string Output);
