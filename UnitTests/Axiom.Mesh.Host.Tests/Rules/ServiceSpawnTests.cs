using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Host.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §4.2 and §9.1 (decisions 26, 27) on a running daemon and its real pipe: for every start of a spawned service
/// a fresh token, valid while that child runs and withdrawn when it exits; the child started in its own state directory with
/// the token, the pipe, the home and the state directory in its environment; its output in the daemon's log, the token in
/// none of it; after the drain a child that stays is killed; and the echo service, started under <c>dotnet</c>, connects as
/// its name, answers a call and is restarted after it was killed.
/// </summary>
public sealed class ServiceSpawnTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    // §4.2: each start gets a token of its own; it proves the name while its child runs and nothing once it exited; the
    // environment names the pipe, the home and a state directory that exists, which is also the working directory.
    [Fact]
    public async Task EveryStartGetsAFreshTokenValidWhileItsChildRuns_WithThePipeHomeAndStateDirectoryInItsEnvironment()
    {
        using var home = new TempHome();
        var processes = new FakeChildProcesses(Worker(home));
        var log = new CapturedLog();
        HostRuntime daemon = await StartAsync(home, processes, log, TimeSpan.FromSeconds(5));
        try
        {
            FakeChildProcesses.FakeChild first = await processes.NextAsync();
            ServiceToken token = TokenOf(first);
            await using (LocalClient child = await ConnectAsync(first, token))
                Assert.Equal(Role.Service, child.Host.Role);
            first.Write("hello from the worker");
            first.Exit(1);
            FakeChildProcesses.FakeChild second = await processes.NextAsync();
            await log.WaitForAsync("service worker: hello from the worker");

            Assert.Equal(ByeReason.Refused, (await Assert.ThrowsAsync<LocalConnectionException>(() => ConnectAsync(second, token))).Reason);
            Assert.False(TokenOf(second).FixedTimeEquals(token));
            await using (LocalClient again = await ConnectAsync(second, TokenOf(second)))
                Assert.Equal(Role.Service, again.Host.Role);
            string state = home.Paths.ServiceState("worker");
            Assert.Equal(state, first.Start.WorkingDirectory);
            Assert.True(Directory.Exists(state));
            Assert.Equal(state, first.Start.Environment["AXIOM_MESH_SERVICE_STATE"]);
            Assert.Equal(home.Paths.Home, first.Start.Environment[HostPaths.HomeVariable]);
            Assert.Equal(daemon.Pipe, first.Start.Environment[PipeLocation.EnvironmentVariable]);
            Assert.Equal(["--quiet"], first.Start.Arguments);
            Assert.DoesNotContain(log.Lines, line => line.Contains(first.Start.Environment[ServiceToken.EnvironmentVariable], StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(first.Start.Environment[ServiceToken.EnvironmentVariable], first.Start.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Release(processes);
            await daemon.DisposeAsync();
        }
    }

    // Plan §9.2 item 5: the daemon stops: the child's connection is drained (it says Bye); a child process that does not exit
    // within the grace after that is killed with everything it started, and the kill is logged.
    [Fact]
    public async Task AChildThatStaysAfterTheDrainIsKilledOnceTheGraceIsOver()
    {
        using var home = new TempHome();
        var processes = new FakeChildProcesses(Worker(home));
        var log = new CapturedLog();
        HostRuntime daemon = await StartAsync(home, processes, log, TimeSpan.FromMilliseconds(200));
        FakeChildProcesses.FakeChild child = await processes.NextAsync();
        LocalClient connected = await ConnectAsync(child, TokenOf(child));
        connected.Draining.Register(() => _ = connected.DisposeAsync().AsTask());

        await daemon.DisposeAsync();

        Assert.Equal(1, child.Kills);
        Assert.Single(processes.Started);
        Assert.True(connected.Draining.IsCancellationRequested);
        Assert.Contains(log.Lines, line => line.Contains("service worker did not exit within 200 ms of the stop and is killed", StringComparison.Ordinal));
        await connected.DisposeAsync();
    }

    // S-D11: an entry whose executable is not there is noted once and not supervised: nothing is started, nothing retried.
    [Fact]
    public async Task AServiceWhoseExecutableIsAbsentIsNotedOnceAndNotStarted()
    {
        using var home = new TempHome();
        var processes = new FakeChildProcesses();
        var log = new CapturedLog();
        HostRuntime daemon = await StartAsync(home, processes, log, TimeSpan.FromSeconds(5));
        await daemon.DisposeAsync();

        Assert.Empty(processes.Started);
        Assert.Single(log.Lines, line => line.Contains("services.worker:", StringComparison.Ordinal) && line.Contains("is not there", StringComparison.Ordinal));
        Assert.Single(log.Lines, line => line.Contains("services.chat:", StringComparison.Ordinal) && line.Contains("is not there", StringComparison.Ordinal));
    }

    // §4.2, §9.1, §9.8 with a real child: the echo service, spawned under dotnet, finds its token, the pipe and its state in
    // its environment, connects as echo, answers a call; killed from outside, it is started again with a new token and
    // answers again.
    [DotnetFact]
    public async Task TheEchoServiceSpawnedUnderDotnetConnectsAnswersACall_AndIsRestartedAfterItWasKilled()
    {
        using var home = new TempHome();
        string dll = Path.Combine(AppContext.BaseDirectory, "Axiom.Mesh.EchoService.dll");
        string dotnet = DotnetFactAttribute.Dotnet!;
        await File.WriteAllTextAsync(home.Paths.ConfigFile, $$"""
            { "mesh": { "port": 0, "listen": "127.0.0.1", "mdns": false, "broadcast": false },
              "services": { "echo": { "spawn": {{Json(dotnet)}}, "args": [{{Json(dll)}}], "kinds": [100], "entryKinds": [100] } } }
            """);
        var log = new CapturedLog();
        HostRuntime daemon = await HostRuntime.StartAsync(Start(home, new SystemChildProcesses(), log, TimeSpan.FromSeconds(10)), CancellationToken.None);
        try
        {
            using var deadline = new CancellationTokenSource(Patience);
            int firstPid = PidOf(await log.WaitForAsync("service echo started: pid"));
            await using LocalClient app = await LocalClient.OpenAsync(await LocalPipe.ConnectAsync(LocationOf(daemon), deadline.Token), new LocalClientOptions { Name = "spawn-test" }, deadline.Token);
            Assert.True((await app.SendAsync(new CreateIdentity { UserName = "Alice", DeviceName = "laptop" }, deadline.Token)).IsOk);
            Assert.Equal(new byte[] { 1, 2, 3 }, await CallEchoAsync(app, [1, 2, 3], deadline.Token));

            using (Process child = Process.GetProcessById(firstPid))
                child.Kill(entireProcessTree: true);
            await log.WaitForAsync("service echo exited with");
            string restarted = await WaitForSecondStartAsync(log);

            Assert.NotEqual(firstPid, PidOf(restarted));
            Assert.Equal(new byte[] { 4 }, await CallEchoAsync(app, [4], deadline.Token));
            Assert.True(Directory.Exists(Path.Combine(home.Paths.ServiceState("echo"), "index")));
        }
        finally
        {
            await daemon.DisposeAsync();
        }

        Assert.Contains(log.Lines, line => line.Contains("service echo exited with 0: the daemon stops", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, line => line.Contains("is given up", StringComparison.Ordinal));
    }

    private static string Worker(TempHome home) => Path.Combine(home.Path, "bin", OperatingSystem.IsWindows() ? "worker.exe" : "worker");

    private static async Task<HostRuntime> StartAsync(TempHome home, FakeChildProcesses processes, CapturedLog log, TimeSpan grace)
    {
        await File.WriteAllTextAsync(home.Paths.ConfigFile, """
            { "mesh": { "port": 0, "listen": "127.0.0.1", "mdns": false, "broadcast": false },
              "services": {
                "worker": { "spawn": "worker", "args": ["--quiet"], "kinds": [100], "entryKinds": [100] },
                "chat": { "spawn": "axiom-mesh-chat", "kinds": [2], "entryKinds": [2, 3] } } }
            """);
        return await HostRuntime.StartAsync(Start(home, processes, log, grace) with { HostDirectory = Path.Combine(home.Path, "bin") }, CancellationToken.None);
    }

    private static HostStart Start(TempHome home, IChildProcesses processes, CapturedLog log, TimeSpan grace)
    {
        string pipe = OperatingSystem.IsWindows() ? "axiom-mesh-spawn-" + Guid.NewGuid().ToString("N")[..12] : Path.Combine(home.Path, "spawn.sock");
        return new HostStart
        {
            Paths = home.Paths,
            Environment = name => name == PipeLocation.EnvironmentVariable ? pipe : Environment.GetEnvironmentVariable(name),
            Version = "0.2.0-test",
            ConsoleLog = false,
            Processes = processes,
            AlsoLogTo = log,
            ServiceGrace = grace,
        };
    }

    private static PipeLocation LocationOf(HostRuntime daemon) => PipeLocation.Resolve(HostPlatform.Current, new PipeEnvironment { Pipe = daemon.Pipe });

    private static ServiceToken TokenOf(FakeChildProcesses.FakeChild child) =>
        ServiceToken.TryParseHex(child.Start.Environment[ServiceToken.EnvironmentVariable], out ServiceToken token) ? token : throw new InvalidOperationException("no token");

    // The child's own connection: the pipe and the token from its environment, as the service runtime does it.
    private static async Task<LocalClient> ConnectAsync(FakeChildProcesses.FakeChild child, ServiceToken token)
    {
        using var deadline = new CancellationTokenSource(Patience);
        PipeLocation pipe = PipeLocation.Resolve(HostPlatform.Current, new PipeEnvironment { Pipe = child.Start.Environment[PipeLocation.EnvironmentVariable] });
        return await LocalClient.OpenAsync(await LocalPipe.ConnectAsync(pipe, deadline.Token), new LocalClientOptions { Role = Role.Service, Name = "worker", Token = token }, deadline.Token);
    }

    // Children the test never ended exit when the test is over, so that the daemon's stop does not wait for them.
    private static void Release(FakeChildProcesses processes)
    {
        foreach (FakeChildProcesses.FakeChild child in processes.Started)
            child.Exit(0);
    }

    private static async Task<byte[]> CallEchoAsync(LocalClient app, byte[] payload, CancellationToken cancellationToken)
    {
        // The service may still be connecting: ServiceUnavailable until it is (LOCAL-SPEC.md §6.7).
        while (true)
        {
            LocalResult<ReadOnlyMemory<byte>> result = await app.SendAsync(new ServiceCall { Service = "echo", Method = "echo", Payload = payload }, cancellationToken);
            if (result.IsOk)
                return result.Value.ToArray();
            Assert.Equal(Status.ServiceUnavailable, result.Status);
            await Task.Yield();
        }
    }

    private static async Task<string> WaitForSecondStartAsync(CapturedLog log)
    {
        using var deadline = new CancellationTokenSource(Patience);
        while (log.Lines.Count(l => l.Contains("service echo started: pid", StringComparison.Ordinal)) < 2)
        {
            deadline.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        return log.Lines.Last(l => l.Contains("service echo started: pid", StringComparison.Ordinal));
    }

    private static int PidOf(string line) => int.Parse(Regex.Match(line, @"pid (\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

    private static string Json(string text) => System.Text.Json.JsonSerializer.Serialize(text);
}
