using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Host.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Host.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §4.2, §9.1, §9.2 and SYNC-SERVICE.md §2 with the real sync service: a daemon spawns it under <c>dotnet</c> as it
/// spawns <c>axiom-mesh-sync</c>; the child finds its token in its environment, registers the Archive kind, and a folder bound
/// over the real pipe is published as a Version.
/// </summary>
public sealed class SyncServiceTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DotnetFact]
    public async Task TheSyncServiceSpawnedByTheDaemonRegistersArchives_AndPublishesABoundFolder()
    {
        using var home = new TempHome();
        string dll = Path.Combine(AppContext.BaseDirectory, "Axiom.Mesh.Sync.dll");
        string folder = Path.Combine(home.Path, "shared");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "synced by the spawned service");
        await File.WriteAllTextAsync(home.Paths.ConfigFile, $$"""
            { "mesh": { "port": 0, "listen": "127.0.0.1", "mdns": false, "broadcast": false },
              "services": { "sync": { "spawn": {{Json(DotnetFactAttribute.Dotnet!)}}, "args": [{{Json(dll)}}], "kinds": [1], "entryKinds": [1] } } }
            """);
        string pipe = OperatingSystem.IsWindows() ? "axiom-mesh-sync-" + Guid.NewGuid().ToString("N")[..12] : Path.Combine(home.Path, "sync.sock");
        var log = new CapturedLog();
        HostRuntime daemon = await HostRuntime.StartAsync(new HostStart
        {
            Paths = home.Paths,
            Environment = name => name == PipeLocation.EnvironmentVariable ? pipe : Environment.GetEnvironmentVariable(name),
            Version = "0.2.0-test",
            ConsoleLog = false,
            Processes = new SystemChildProcesses(),
            AlsoLogTo = log,
            ServiceGrace = TimeSpan.FromSeconds(10),
        }, CancellationToken.None);
        try
        {
            using var deadline = new CancellationTokenSource(Patience);
            await log.WaitForAsync("service sync started: pid");
            PipeLocation location = PipeLocation.Resolve(HostPlatform.Current, new PipeEnvironment { Pipe = daemon.Pipe });
            await using LocalClient app = await LocalClient.OpenAsync(await LocalPipe.ConnectAsync(location, deadline.Token), new LocalClientOptions { Name = "sync-spawn-test" }, deadline.Token);
            Assert.True((await app.SendAsync(new CreateIdentity { UserName = "Alice", DeviceName = "laptop" }, deadline.Token)).IsOk);
            LocalResult<Package> archive = await app.SendAsync(new CreatePackage { Kind = PackageKind.Archive, Name = "Shared" }, deadline.Token);
            Assert.True(archive.IsOk, archive.ToString());

            LocalResult<BindReply> bound = await BindAsync(app, folder, archive.Value.PackageId, deadline.Token);
            Binding published = await PublishedAsync(app, folder, deadline.Token);
            Local.Service sync = (await app.SendAsync(new ListServices(), deadline.Token)).Value.Single(s => s.Name == SyncProtocol.ServiceName);

            Assert.True(bound.IsOk, bound.ToString());
            Assert.Equal((true, true), (sync.Connected, sync.Spawned));
            Assert.Equal([PackageKind.Archive], sync.Kinds);
            Assert.Equal(1ul, published.Files);
            Assert.True(File.Exists(Path.Combine(folder, SyncProtocol.MarkerName)));
            Assert.True(Directory.Exists(Path.Combine(home.Paths.ServiceState("sync"), "index")));
        }
        finally
        {
            await daemon.DisposeAsync();
        }

        Assert.DoesNotContain(log.Lines, line => line.Contains("is given up", StringComparison.Ordinal));
    }

    // The service may still be connecting: ServiceUnavailable until it is (LOCAL-SPEC.md §6.7).
    private static async Task<LocalResult<BindReply>> BindAsync(LocalClient app, string folder, PackageId archive, CancellationToken cancellationToken)
    {
        while (true)
        {
            LocalResult<BindReply> bound = await SyncCall.SendAsync(app, new BindRequest { Directory = folder, PackageId = archive }, cancellationToken);
            if (bound.Status != Status.ServiceUnavailable)
                return bound;
            await Task.Yield();
        }
    }

    private static async Task<Binding> PublishedAsync(LocalClient app, string folder, CancellationToken cancellationToken)
    {
        while (true)
        {
            LocalResult<StatusReply> status = await SyncCall.SendAsync(app, new StatusRequest { Directory = folder }, cancellationToken);
            if (status.IsOk && status.Value.Bindings is [{ Pending: false } binding] && binding.Head != default)
                return binding;
            await Task.Yield();
        }
    }

    private static string Json(string text) => System.Text.Json.JsonSerializer.Serialize(text);
}
