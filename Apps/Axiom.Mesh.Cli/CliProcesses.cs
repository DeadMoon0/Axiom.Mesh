using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Cli;

/// <summary>The processes of the machine as <c>host install</c> uses them by default: running a program, stopping the daemon.</summary>
internal static class CliProcesses
{
    /// <summary>Runs a program and waits for it, reading what it prints.</summary>
    /// <param name="program">The program, found on <c>PATH</c>.</param>
    /// <param name="arguments">Its arguments, each passed as one.</param>
    /// <param name="cancellationToken">Stops waiting (the program is killed).</param>
    /// <returns>Its exit code and output; 127 when it could not be started.</returns>
    public static async Task<ProgramRun> RunAsync(string program, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new Win32Exception($"{program} did not start");
        }
        catch (Win32Exception e)
        {
            return new ProgramRun { ExitCode = 127, Output = $"{program} cannot be started: {e.Message}" };
        }

        using (process)
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            string text = ((await output.ConfigureAwait(false)) + Environment.NewLine + (await error.ConfigureAwait(false))).Trim();
            return new ProgramRun { ExitCode = process.ExitCode, Output = text };
        }
    }

    /// <summary>Stops every process this user may see that runs an executable, waiting up to 5 seconds for each.</summary>
    /// <param name="executable">The executable, absolute.</param>
    /// <returns>How many were stopped.</returns>
    public static int StopAll(string executable)
    {
        int stopped = 0;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try
                {
                    if (!string.Equals(process.MainModule?.FileName, executable, comparison))
                        continue;
                    process.Kill();
                    process.WaitForExit(5_000);
                    stopped++;
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Another user's process, or one that ended meanwhile: not ours to stop.
                }
            }
        }

        return stopped;
    }
}
