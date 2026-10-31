using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Cli;

/// <summary>
/// Runs an <see cref="AutostartPlan"/> on a <see cref="CliMachine"/>, step by step, and decides nothing: a program that
/// fails where the plan does not allow it ends the run as a <see cref="CliFailure"/> naming the step and what the program
/// said.
/// </summary>
internal static class AutostartShell
{
    /// <summary>Runs the plan.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="machine">The machine.</param>
    /// <param name="cancellationToken">Stops waiting for a program.</param>
    /// <returns>What each step did.</returns>
    /// <exception cref="CliFailure">A step failed that the plan does not allow to fail.</exception>
    public static async Task<AutostartOutcome> RunAsync(AutostartPlan plan, CliMachine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(machine);
        var done = new List<StepOutcome>();
        bool removed = false;
        int stopped = 0;
        foreach (AutostartStep step in plan.Steps)
        {
            switch (step)
            {
                case WriteStep write:
                    await WriteAsync(machine, write, cancellationToken).ConfigureAwait(false);
                    done.Add(new StepOutcome(step, 0, string.Empty));
                    break;
                case DeleteStep delete:
                    bool existed = Delete(machine, delete);
                    removed |= delete.Removes && existed;
                    done.Add(new StepOutcome(step, 0, existed ? "deleted" : "absent"));
                    break;
                case StopStep stop:
                    int count = machine.StopProcesses(stop.Daemon);
                    stopped += count;
                    done.Add(new StepOutcome(step, 0, $"{count} stopped"));
                    break;
                case RunStep run:
                    ProgramRun result = await machine.RunProgram(run.Program, run.Arguments, cancellationToken).ConfigureAwait(false);
                    if (result.ExitCode != 0 && !run.MayFail)
                        throw CliFailure.Problem($"'{run.Describe()}' failed with exit {result.ExitCode}: {FirstLine(result.Output)}{Hint(plan.System)}");
                    removed |= run.Removes && result.ExitCode == 0;
                    done.Add(new StepOutcome(step, result.ExitCode, result.Output));
                    break;
            }
        }

        return new AutostartOutcome(plan, done, removed, stopped);
    }

    /// <summary>The bytes a write step writes: UTF-16 little endian with a byte order mark, or UTF-8 without one.</summary>
    /// <param name="write">The step.</param>
    /// <returns>The bytes.</returns>
    public static byte[] BytesOf(WriteStep write)
    {
        ArgumentNullException.ThrowIfNull(write);
        return write.Utf16 ? [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(write.Text)] : new UTF8Encoding(false).GetBytes(write.Text);
    }

    private static async Task WriteAsync(CliMachine machine, WriteStep write, CancellationToken cancellationToken)
    {
        try
        {
            await machine.WriteFile(write.Path, BytesOf(write), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw CliFailure.Problem($"cannot write {write.Path}: {e.Message}");
        }
    }

    private static bool Delete(CliMachine machine, DeleteStep delete)
    {
        try
        {
            return machine.DeleteFile(delete.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw CliFailure.Problem($"cannot delete {delete.Path}: {e.Message}");
        }
    }

    private static string FirstLine(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "it printed nothing";

    private static string Hint(AutostartSystem system) => system == AutostartSystem.SystemdUser
        ? "; a login session has a systemd user manager, a container or a plain ssh shell may not: start 'axiom-meshd run' yourself there"
        : string.Empty;
}
