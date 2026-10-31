using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// What <c>axiom-mesh host install</c> and <c>uninstall</c> touch of the machine: the platform and account, where the
/// daemon is, the programs that register a logon start (<c>schtasks</c>, <c>systemctl</c>), the files they write, and the
/// running daemon. The process shell fills it with the real machine (<see cref="OfThisProcess"/>); a test with fakes,
/// so the registration is planned and checked without touching the machine.
/// </summary>
public sealed class CliMachine
{
    /// <summary>The platform, which decides the autostart: a scheduled task on Windows, a systemd user unit on Linux.</summary>
    public required LocalPlatform Platform { get; init; }

    /// <summary>The account whose logon starts the Host on Windows, <c>DOMAIN\user</c>.</summary>
    public required string Account { get; init; }

    /// <summary>The daemon <c>host install</c> registers unless <c>--daemon</c> names another: <c>axiom-meshd</c> next to this tool.</summary>
    public required string DaemonPath { get; init; }

    /// <summary>Where a task definition is written for <c>schtasks</c> and deleted again.</summary>
    public required string TemporaryDirectory { get; init; }

    /// <summary>Runs a program with its arguments and waits for it: its exit code and what it printed.</summary>
    public required Func<string, IReadOnlyList<string>, CancellationToken, Task<ProgramRun>> RunProgram { get; init; }

    /// <summary>Writes a file, replacing it and creating its directory.</summary>
    public required Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> WriteFile { get; init; }

    /// <summary>Deletes a file; <see langword="true"/> when it existed.</summary>
    public required Func<string, bool> DeleteFile { get; init; }

    /// <summary>Whether a file exists.</summary>
    public required Func<string, bool> FileExists { get; init; }

    /// <summary>Stops every process of this user that runs an executable; how many were stopped.</summary>
    public required Func<string, int> StopProcesses { get; init; }

    /// <summary>The real machine this process runs on.</summary>
    /// <returns>The machine.</returns>
    public static CliMachine OfThisProcess() => new()
    {
        Platform = OperatingSystem.IsWindows() ? LocalPlatform.Windows : OperatingSystem.IsMacOS() ? LocalPlatform.MacOS : LocalPlatform.Linux,
        Account = Environment.UserDomainName + "\\" + Environment.UserName,
        DaemonPath = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "axiom-meshd.exe" : "axiom-meshd"),
        TemporaryDirectory = Path.GetTempPath(),
        RunProgram = CliProcesses.RunAsync,
        WriteFile = CliFiles.WriteCreatingDirectoryAsync,
        DeleteFile = CliFiles.Delete,
        FileExists = File.Exists,
        StopProcesses = CliProcesses.StopAll,
    };
}
