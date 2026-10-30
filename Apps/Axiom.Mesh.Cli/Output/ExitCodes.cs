using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>The exit code each status of LOCAL-SPEC.md §11.1 ends a command with.</summary>
internal static class ExitCodes
{
    /// <summary>The exit code of a status: 0 for <c>OK</c>, 3 for every refusal.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The exit code.</returns>
    public static CliExitCode Of(Status status) => status == Status.Ok ? CliExitCode.Ok : CliExitCode.Refused;
}
