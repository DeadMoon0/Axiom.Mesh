using System;
using System.Collections.Generic;
using System.IO;

namespace Axiom.Mesh.Host;

/// <summary>
/// The command line of <c>axiom-meshd</c>, the daemon. Everything the process reads from outside (its arguments, its
/// version, its output streams) is handed in, so a command is a function of its inputs.
/// </summary>
public static class HostProgram
{
    /// <summary>The name the daemon is started by.</summary>
    public const string CommandName = "axiom-meshd";

    /// <summary>Runs one command line.</summary>
    /// <param name="args">The arguments after the program name.</param>
    /// <param name="output">Where results go (standard output).</param>
    /// <param name="error">Where usage and failures go (standard error).</param>
    /// <param name="version">The daemon's informational version.</param>
    /// <returns>What the process exits with.</returns>
    public static HostExitCode Run(IReadOnlyList<string> args, TextWriter output, TextWriter error, string version)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        if (args is ["version"])
        {
            output.WriteLine(version);
            return HostExitCode.Ok;
        }

        error.WriteLine("usage: " + CommandName + " version");
        error.WriteLine();
        error.WriteLine("  version   print the version of this daemon");
        return HostExitCode.Usage;
    }
}
