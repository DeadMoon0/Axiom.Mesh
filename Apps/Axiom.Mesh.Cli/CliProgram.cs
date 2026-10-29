using System;
using System.Collections.Generic;
using System.IO;

namespace Axiom.Mesh.Cli;

/// <summary>
/// The command line of <c>axiom-mesh</c>. Everything the process reads from outside (its arguments, its version, its
/// output streams) is handed in, so a command is a function of its inputs and runs in-process in tests.
/// </summary>
public static class CliProgram
{
    /// <summary>The name the command line is started by.</summary>
    public const string CommandName = "axiom-mesh";

    /// <summary>Runs one command line.</summary>
    /// <param name="args">The arguments after the program name.</param>
    /// <param name="output">Where results go (standard output).</param>
    /// <param name="error">Where usage and failures go (standard error).</param>
    /// <param name="version">The tool's informational version.</param>
    /// <returns>What the process exits with.</returns>
    public static CliExitCode Run(IReadOnlyList<string> args, TextWriter output, TextWriter error, string version)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        if (args is ["--version"])
        {
            output.WriteLine(version);
            return CliExitCode.Ok;
        }

        error.WriteLine("usage: " + CommandName + " --version");
        error.WriteLine();
        error.WriteLine("  --version   print the version of this tool");
        return CliExitCode.Usage;
    }
}
