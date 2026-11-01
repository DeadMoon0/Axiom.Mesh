using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Host.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host.Tests.Regression;

/// <summary>
/// The daemon's stop order (<c>HostRuntime.DisposeAsync</c>, plan §9.2 item 5): its supervisors are marked stopping before
/// the pipe and the node stop, so the exit of a child the node's drain sent home is judged as the daemon stopping.
/// </summary>
public sealed class SupervisorStopOrderTests
{
    // Damage: the supervisors were told only after the node had drained, so a spawned service that exited on the drain, as
    // the runtime makes it, counted as a crash: it was restarted into a stopping daemon, and then killed.
    [Fact]
    public async Task AChildThatExitsOnTheDrainIsStoppedNotRestarted()
    {
        using var home = new TempHome();
        string worker = Path.Combine(home.Path, "bin", OperatingSystem.IsWindows() ? "worker.exe" : "worker");
        var processes = new FakeChildProcesses(worker);
        var log = new CapturedLog();
        await File.WriteAllTextAsync(home.Paths.ConfigFile, """
            { "mesh": { "port": 0, "listen": "127.0.0.1", "mdns": false, "broadcast": false },
              "services": { "worker": { "spawn": "worker", "kinds": [100], "entryKinds": [100] } } }
            """);
        string pipe = OperatingSystem.IsWindows() ? "axiom-mesh-stop-" + Guid.NewGuid().ToString("N")[..12] : Path.Combine(home.Path, "stop.sock");
        HostRuntime daemon = await HostRuntime.StartAsync(
            new HostStart
            {
                Paths = home.Paths,
                Environment = name => name == PipeLocation.EnvironmentVariable ? pipe : Environment.GetEnvironmentVariable(name),
                Version = "0.2.0-test",
                ConsoleLog = false,
                Processes = processes,
                HostDirectory = Path.Combine(home.Path, "bin"),
                AlsoLogTo = log,
                ServiceGrace = TimeSpan.FromSeconds(30),
            },
            CancellationToken.None);
        FakeChildProcesses.FakeChild child = await processes.NextAsync();
        ServiceToken.TryParseHex(child.Start.Environment[ServiceToken.EnvironmentVariable], out ServiceToken token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        LocalClient connection = await LocalClient.OpenAsync(
            await LocalPipe.ConnectAsync(PipeLocation.Resolve(HostPlatform.Current, new PipeEnvironment { Pipe = pipe }), deadline.Token),
            new LocalClientOptions { Role = Role.Service, Name = "worker", Token = token },
            deadline.Token);

        // As the service runtime does for a spawned child: Bye on the drain, and the process exits.
        connection.Draining.Register(() => _ = ExitAsync(connection, child));
        await daemon.DisposeAsync();

        Assert.Single(processes.Started);
        Assert.Equal(0, child.Kills);
        Assert.Contains(log.Lines, line => line.Contains("service worker exited with 0: the daemon stops", StringComparison.Ordinal));
    }

    private static async Task ExitAsync(LocalClient connection, FakeChildProcesses.FakeChild child)
    {
        await connection.DisposeAsync();
        child.Exit(0);
    }
}
