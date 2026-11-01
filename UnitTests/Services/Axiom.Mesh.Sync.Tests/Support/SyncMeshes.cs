using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Sync.Tests.Support;

/// <summary>
/// In-memory meshes with the sync service configured on every node, and the steps the service's own tests take: share an
/// Archive between Alice and Bob, bind, scan, wait on the binding's status, read the Versions.
/// </summary>
internal static class SyncMeshes
{
    public const string Alice = "alice-laptop";
    public const string Bob = "bob-desktop";

    private static readonly CancellationToken None = CancellationToken.None;

    public static Task<TestMesh> StartAsync(params string[] nodes) =>
        TestMesh.StartAsync(TestMeshOptions.Of(nodes) with { Local = TestServices.With(TestCliService.Local, SyncProgram.Definition) }, None);

    public static SyncServiceOptions Options(TestMesh mesh) => new() { Time = mesh.Clock, Stability = TimeSpan.Zero };

    public static Task<RunningService> RunAsync(TestMesh mesh, string node, SyncServiceOptions options) => mesh[node].RunServiceAsync(new SyncService(options), None);

    public static Task<RunningService> RunAsync(TestMesh mesh, string node) => RunAsync(mesh, node, Options(mesh));

    /// <summary>An Archive Alice owns and Bob follows with the given rights.</summary>
    public static async Task<PackageId> SharedArchiveAsync(TestMesh mesh, GrantRights rights = GrantRights.Write)
    {
        await mesh.PairAsync(Alice, Bob, None);
        await mesh.ConnectAsync(Alice, Bob, None);
        PackageId archive = await mesh[Alice].CreatePackageAsync("Shared", PackageKind.Archive, Retention.Default, None);
        await mesh[Alice].GrantAsync(archive, "bob", rights, None);
        await mesh.SettleAsync(None);
        await mesh[Bob].FollowAsync(archive, None);
        await mesh.SettleAsync(None);
        return archive;
    }

    /// <summary>An Archive only one node holds.</summary>
    public static async Task<PackageId> OwnArchiveAsync(TestMesh mesh, string node) =>
        await mesh[node].CreatePackageAsync("Own", PackageKind.Archive, Retention.Default, None);

    public static async Task<LocalResult<TReply>> CallAsync<TRequest, TReply>(TestMesh mesh, string node, ISyncMethod<TRequest, TReply> request)
        where TRequest : class, ISyncMethod<TRequest, TReply>
        where TReply : class, IWireRecord<TReply>
    {
        await using LocalClient app = await mesh[node].ConnectUserAsync("sync-test", None);
        return await SyncCall.SendAsync(app, request, None);
    }

    public static async Task<Binding> BindAsync(TestMesh mesh, string node, string directory, PackageId archive)
    {
        LocalResult<BindReply> bound = await CallAsync(mesh, node, new BindRequest { Directory = directory, PackageId = archive });
        Assert.True(bound.IsOk, bound.ToString());
        return bound.Value.Binding;
    }

    public static async Task<Binding> StatusAsync(TestMesh mesh, string node, string directory)
    {
        LocalResult<StatusReply> status = await CallAsync(mesh, node, new StatusRequest { Directory = directory });
        Assert.True(status.IsOk, status.ToString());
        return status.Value.Bindings.Single();
    }

    /// <summary>Queues a run and waits until it ended.</summary>
    public static async Task<Binding> ScanAsync(TestMesh mesh, string node, string directory)
    {
        ulong before = (await StatusAsync(mesh, node, directory)).Runs;
        LocalResult<ScanReply> scanned = await CallAsync(mesh, node, new ScanRequest { Directory = directory });
        Assert.True(scanned.IsOk, scanned.ToString());
        return await SettledAsync(mesh, node, directory, b => b.Runs > before);
    }

    /// <summary>Asks the service until no run is pending and the condition holds.</summary>
    public static async Task<Binding> SettledAsync(TestMesh mesh, string node, string directory, Func<Binding, bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using LocalClient app = await mesh[node].ConnectUserAsync("sync-test", None);
        while (true)
        {
            LocalResult<StatusReply> status = await SyncCall.SendAsync(app, new StatusRequest { Directory = directory }, None);
            if (status.IsOk && status.Value.Bindings is [{ Pending: false } binding] && condition(binding))
                return binding;
            if (deadline.IsCancellationRequested)
                throw new TestMeshException($"The binding of {directory} on {node} did not settle as expected (status: {(status.IsOk ? status.Value.Bindings.SingleOrDefault() : status)}).", "Check the service's state.");
            await Task.Yield();
        }
    }

    public static async Task<IReadOnlyList<Entry>> VersionsAsync(TestMesh mesh, string node, PackageId archive)
    {
        await using LocalClient app = await mesh[node].ConnectUserAsync("sync-test", None);
        var versions = new List<Entry>();
        EntryKey? after = null;
        while (true)
        {
            LocalResult<EntryPage> page = await app.SendAsync(new ListHistory { PackageId = archive, After = after, Limit = 64 }, None);
            Assert.True(page.IsOk, page.ToString());
            versions.AddRange(page.Value.Entries.Where(e => EntryPayload.TryParse(e.Kind, e.Payload, out EntryPayload? p) && p is VersionPayload));
            if (!page.Value.More || page.Value.Entries.Count == 0)
                return versions;
            Entry last = page.Value.Entries[^1];
            after = new EntryKey(last.Clock, last.DeviceId, last.Hash);
        }
    }

    public static DeviceId DeviceOf(string node) => TestIdentity.Of(node).DeviceId;
}
