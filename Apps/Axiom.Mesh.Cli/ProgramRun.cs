namespace Axiom.Mesh.Cli;

/// <summary>How a program <see cref="CliMachine.RunProgram"/> ran ended: its exit code and what it printed.</summary>
public sealed class ProgramRun
{
    /// <summary>The exit code; 0 is success. A program that could not be started is 127.</summary>
    public required int ExitCode { get; init; }

    /// <summary>Standard output and standard error together, trimmed.</summary>
    public required string Output { get; init; }
}
