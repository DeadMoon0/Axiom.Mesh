using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Cli.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// <c>axiom-mesh host install|uninstall</c> (fundamentals plan §6e) on a fake machine: it registers what the plan says
/// without asking the Host, explains every failure in one line (fundamentals plan §2.7), and answers as JSON with
/// <c>--json</c>.
/// </summary>
public sealed class HostInstallTests
{
    private const string Daemon = "/home/alice/.local/bin/axiom-meshd";

    // §6e: install writes the unit, runs systemctl, starts the Host and says so; the Host's pipe is never opened.
    [Fact]
    public async Task InstallRegistersAndStartsTheHostWithoutAskingIt()
    {
        var machine = new FakeMachine(LocalPlatform.Linux) { Home = "/srv/mesh/" };

        (CliExitCode exit, string output, string error) = await machine.RunAsync("host", "install");

        Assert.Equal(CliExitCode.Ok, exit);
        Assert.Empty(error);
        Assert.Equal(["systemctl --user daemon-reload", "systemctl --user enable axiom-meshd.service", "systemctl --user restart axiom-meshd.service"], machine.Ran);
        Assert.Contains("ExecStart=\"/home/alice/.local/bin/axiom-meshd\" \"run\" \"--home\" \"/srv/mesh\"", machine.Written["/home/alice/.config/systemd/user/axiom-meshd.service"], StringComparison.Ordinal);
        Assert.StartsWith("axiom-meshd starts at logon: systemd user unit /home/alice/.config/systemd/user/axiom-meshd.service", output, StringComparison.Ordinal);
        Assert.Contains("started  now", output, StringComparison.Ordinal);
        Assert.Equal(0, machine.Connections);
    }

    // §2.7: a program that fails where the plan does not allow it ends the command with exit 3 and one line naming it.
    [Fact]
    public async Task ASystemProgramThatFailsIsOneLineAndExit3()
    {
        var machine = new FakeMachine(LocalPlatform.Linux) { Fail = "daemon-reload", FailOutput = "Failed to connect to bus: No medium found" };

        (CliExitCode exit, string output, string error) = await machine.RunAsync("host", "install");

        Assert.Equal(CliExitCode.Refused, exit);
        Assert.Empty(output);
        Assert.StartsWith("axiom-mesh: 'systemctl --user daemon-reload' failed with exit 1: Failed to connect to bus: No medium found; ", error, StringComparison.Ordinal);
        Assert.Single(error.TrimEnd('\n').Split('\n'));
    }

    // §2.7: without the daemon next to the tool (a dotnet tool install) the command says how to name it; on a platform
    // without an autostart it says to run the daemon by hand.
    [Fact]
    public async Task AMissingDaemonAndAPlatformWithoutAutostartAreExplained()
    {
        var missing = new FakeMachine(LocalPlatform.Windows) { DaemonExists = false };
        (CliExitCode exit, _, string error) = await missing.RunAsync("host", "install");
        Assert.Equal(CliExitCode.Refused, exit);
        Assert.Contains("name the daemon with --daemon <path>", error, StringComparison.Ordinal);
        Assert.Empty(missing.Ran);

        (CliExitCode macExit, _, string macError) = await new FakeMachine(LocalPlatform.MacOS).RunAsync("host", "install");
        Assert.Equal(CliExitCode.Refused, macExit);
        Assert.Contains("not available on MacOS yet; start it with 'axiom-meshd run'", macError, StringComparison.Ordinal);
    }

    // §6e on Windows: the task definition goes to schtasks as UTF-16 and is deleted again; --no-start only registers.
    [Fact]
    public async Task OnWindowsTheDefinitionIsWrittenAsUtf16RegisteredAndDeleted()
    {
        var machine = new FakeMachine(LocalPlatform.Windows);

        (CliExitCode exit, string output, _) = await machine.RunAsync("host", "install", "--no-start");

        Assert.Equal(CliExitCode.Ok, exit);
        Assert.Equal([@"schtasks /Create /TN \Axiom.Mesh\axiom-meshd /XML C:\Temp\axiom-meshd.task.xml /F"], machine.Ran);
        Assert.Equal([@"C:\Temp\axiom-meshd.task.xml"], machine.Deleted);
        Assert.Contains(@"&quot;C:\bin\axiom-meshd.exe&quot; run</Arguments>", machine.Written[@"C:\Temp\axiom-meshd.task.xml"], StringComparison.Ordinal);
        Assert.Equal([0xFF, 0xFE], machine.Bytes[@"C:\Temp\axiom-meshd.task.xml"][..2]);
        Assert.Contains("started  no (--no-start)", output, StringComparison.Ordinal);
    }

    // An uninstall with nothing registered is no failure and says so; with a task it says it was removed. --json shows
    // whether something was removed and every step's result.
    [Fact]
    public async Task UninstallSaysWhetherSomethingWasRemoved_NothingRegisteredIsNoFailure()
    {
        var nothing = new FakeMachine(LocalPlatform.Windows) { Fail = "/Delete", FailOutput = "ERROR: The system cannot find the file specified." };
        (CliExitCode exit, string output, string error) = await nothing.RunAsync("host", "uninstall");
        Assert.Equal(CliExitCode.Ok, exit);
        Assert.Empty(error);
        Assert.StartsWith(@"nothing to remove: no scheduled task \Axiom.Mesh\axiom-meshd was registered", output, StringComparison.Ordinal);

        var registered = new FakeMachine(LocalPlatform.Windows) { Running = 1 };
        (CliExitCode removedExit, string json, _) = await registered.RunAsync("host", "uninstall", "--json");
        Assert.Equal(CliExitCode.Ok, removedExit);
        Assert.Contains("\"removed\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"stopped\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"system\": \"scheduled-task\"", json, StringComparison.Ordinal);
        Assert.Contains(@"""step"": ""schtasks /Delete /TN \\Axiom.Mesh\\axiom-meshd /F""", json, StringComparison.Ordinal);
    }

    /// <summary>A machine that records what the command does to it.</summary>
    private sealed class FakeMachine(LocalPlatform platform)
    {
        public List<string> Ran { get; } = [];

        public Dictionary<string, string> Written { get; } = [];

        public Dictionary<string, byte[]> Bytes { get; } = [];

        public List<string> Deleted { get; } = [];

        public bool DaemonExists { get; init; } = true;

        public string? Home { get; init; }

        public string? Fail { get; init; }

        public string FailOutput { get; init; } = string.Empty;

        public int Running { get; init; }

        public int Connections { get; private set; }

        public async Task<(CliExitCode Exit, string Output, string Error)> RunAsync(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var environment = new CliEnvironment
            {
                Version = "0.1.0",
                HostAddress = Offline.Pipe,
                ConnectHost = _ =>
                {
                    Connections++;
                    return Task.FromException<Stream>(new IOException("no Host"));
                },
                Variables = name => name switch { "HOME" => "/home/alice", "AXIOM_MESH_HOME" => Home, _ => null },
                CurrentDirectory = platform == LocalPlatform.Windows ? @"C:\work" : "/work",
                Machine = Machine(),
            };
            CliExitCode exit = await CliProgram.RunAsync(args, output, error, environment, CancellationToken.None);
            return (exit, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
        }

        private CliMachine Machine() => new()
        {
            Platform = platform,
            Account = @"LAPTOP\alice",
            DaemonPath = platform == LocalPlatform.Windows ? @"C:\bin\axiom-meshd.exe" : Daemon,
            TemporaryDirectory = @"C:\Temp",
            RunProgram = (program, arguments, _) =>
            {
                string line = string.Join(' ', arguments.Prepend(program));
                Ran.Add(line);
                bool fails = Fail is not null && arguments.Contains(Fail);
                return Task.FromResult(new ProgramRun { ExitCode = fails ? 1 : 0, Output = fails ? FailOutput : string.Empty });
            },
            WriteFile = (path, bytes, _) =>
            {
                Bytes[path] = bytes.ToArray();
                Written[path] = bytes.Span.StartsWith(new byte[] { 0xFF, 0xFE }) ? Encoding.Unicode.GetString(bytes.Span[2..]) : Encoding.UTF8.GetString(bytes.Span);
                return Task.CompletedTask;
            },
            DeleteFile = path =>
            {
                Deleted.Add(path);
                return Written.ContainsKey(path);
            },
            FileExists = _ => DaemonExists,
            StopProcesses = _ => Running,
        };
    }
}
