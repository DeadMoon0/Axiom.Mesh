using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// The command line of <c>axiom-mesh</c> (fundamentals plan §2.7). Everything the process reads from outside (its
/// arguments, the pipe to the Host, the configuration, the terminal, the clock) is handed in, so a command is a function
/// of its inputs and runs in-process in tests. Every failure is one line on standard error and an exit code: 1 usage,
/// 2 Host not reachable, 3 refused, 4 timeout; never a stack trace.
/// </summary>
public static class CliProgram
{
    /// <summary>The name the command line is started by.</summary>
    public const string CommandName = "axiom-mesh";

    /// <summary>Runs one command line.</summary>
    /// <param name="args">The arguments after the program name.</param>
    /// <param name="output">Where results go (standard output).</param>
    /// <param name="error">Where usage, progress and failures go (standard error).</param>
    /// <param name="environment">The Host, the configuration, the terminal and the clock.</param>
    /// <param name="cancellationToken">Ctrl-C: ends <c>watch</c> and stops waiting elsewhere.</param>
    /// <returns>What the process exits with.</returns>
    public static async Task<CliExitCode> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, CliEnvironment environment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(environment);

        ParseResult parsed = CommandLine.Parse(args);
        if (parsed is not ParsedCommand { Command: var command })
            return Usage(error, (UsageError)parsed);

        await using var context = new CommandContext(environment, output, error, cancellationToken);
        try
        {
            return await command.RunAsync(context).ConfigureAwait(false);
        }
        catch (CliFailure failure)
        {
            error.WriteLine($"{CommandName}: {failure.Line}");
            return failure.ExitCode;
        }
        catch (LocalConnectionException ended)
        {
            error.WriteLine($"{CommandName}: {ended.FriendlyMessage.ReplaceLineEndings(" ")}");
            return CliExitCode.HostNotReachable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error.WriteLine($"{CommandName}: interrupted before the Host answered");
            return CliExitCode.Timeout;
        }
    }

    private static CliExitCode Usage(TextWriter error, UsageError usage)
    {
        error.WriteLine($"{CommandName}: {usage.Problem}");
        error.WriteLine(usage.Spec is null ? $"usage: {CommandName} <command> [arguments] [options]" : "usage: " + usage.Spec.Usage);
        error.WriteLine($"run '{CommandName} help{(usage.Spec is null ? string.Empty : " " + usage.Spec.Name)}' for more");
        return CliExitCode.Usage;
    }
}
