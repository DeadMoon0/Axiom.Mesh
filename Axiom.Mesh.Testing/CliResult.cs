using Axiom.Mesh.Cli;

namespace Axiom.Mesh.Testing;

/// <summary>What one in-process run of <c>axiom-mesh</c> left (<see cref="TestNode.CliAsync(string, System.Threading.CancellationToken)"/>).</summary>
/// <param name="ExitCode">What the process would exit with.</param>
/// <param name="Stdout">Standard output, lines ended by <c>\n</c>.</param>
/// <param name="Stderr">Standard error, lines ended by <c>\n</c>.</param>
[ClosedShape("The three things a process leaves: its exit code and its two outputs.")]
public sealed record CliResult(CliExitCode ExitCode, string Stdout, string Stderr)
{
    /// <inheritdoc />
    public override string ToString() => $"exit {(int)ExitCode} ({ExitCode})\nstdout:\n{Stdout}stderr:\n{Stderr}";
}
