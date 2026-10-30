using System.Collections.Generic;
using System.Threading.Tasks;

namespace Axiom.Mesh.Cli;

/// <summary>
/// One command of the command line, its values parsed (<see cref="CommandLine"/>). Running it reaches the Host through the
/// <see cref="CommandContext"/>; a failure leaves as a <see cref="CliFailure"/>.
/// </summary>
internal abstract record Command
{
    /// <summary>Runs the command.</summary>
    /// <param name="context">The Host, the output, the environment.</param>
    /// <returns>The exit code of a success; failures leave as a <see cref="CliFailure"/>.</returns>
    public abstract Task<CliExitCode> RunAsync(CommandContext context);

    /// <summary>Writes lines to standard output.</summary>
    /// <param name="context">The context.</param>
    /// <param name="lines">The lines.</param>
    /// <returns><see cref="CliExitCode.Ok"/>.</returns>
    protected static CliExitCode Print(CommandContext context, IEnumerable<string> lines)
    {
        foreach (string line in lines)
            context.Output.WriteLine(line);
        return CliExitCode.Ok;
    }

    /// <summary>Writes one line to standard output.</summary>
    /// <param name="context">The context.</param>
    /// <param name="line">The line.</param>
    /// <returns><see cref="CliExitCode.Ok"/>.</returns>
    protected static CliExitCode Print(CommandContext context, string line)
    {
        context.Output.WriteLine(line);
        return CliExitCode.Ok;
    }
}
