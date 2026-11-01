using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Sync.Tests.Support;
using Axiom.Mesh.Testing;
using static Axiom.Mesh.Sync.Tests.Support.SyncMeshes;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// The namespace <c>axiom-mesh-sync/1</c> end to end on one in-memory node (SYNC-SERVICE.md §3, §5, §6, §7): a change in a bound
/// directory starts a run once it was quiet for two seconds of the service's clock; every method refuses with its own status;
/// events report bindings and conflicts; conflicts page oldest first.
/// </summary>
public sealed class SyncServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §3: the watcher's report starts no run at once; two quiet seconds later one runs and publishes the change.
    [Fact]
    public async Task AChangeStartsARunAfterTwoQuietSecondsOfTheServicesClock()
    {
        await using TestMesh mesh = await StartAsync(Alice);
        using var folders = new Folders();
        var watchers = new FakeWatchers();
        PackageId archive = await OwnArchiveAsync(mesh, Alice);
        await using RunningService sync = await RunAsync(mesh, Alice, Options(mesh) with { Watchers = watchers });
        string directory = folders.Path("alice");
        await BindAsync(mesh, Alice, directory, archive);
        Binding first = await SettledAsync(mesh, Alice, directory, b => b.Runs == 1);

        folders.Write("alice/new.txt", "new");
        watchers.Change(directory);
        mesh.Clock.Advance(TimeSpan.FromSeconds(1.9));
        Binding early = await StatusAsync(mesh, Alice, directory);
        mesh.Clock.Advance(TimeSpan.FromSeconds(0.2));
        Binding published = await SettledAsync(mesh, Alice, directory, b => b.Runs >= 2 && b.Head != first.Head);

        Assert.Equal((1ul, false), (early.Runs, early.Pending));
        Assert.NotEqual(first.Head, published.Head);
        Assert.Equal(1ul, published.Files);
    }

    // §7: each method refuses what it cannot do with the status the document names.
    [Fact]
    public async Task EveryMethodRefusesWithItsOwnStatus()
    {
        await using TestMesh mesh = await StartAsync(Alice);
        using var folders = new Folders();
        PackageId archive = await OwnArchiveAsync(mesh, Alice);
        PackageId channel = await mesh[Alice].CreatePackageAsync("Talk", PackageKind.Channel, Retention.Default, None);
        await using RunningService sync = await RunAsync(mesh, Alice);
        string directory = folders.Path("alice");
        await BindAsync(mesh, Alice, directory, archive);
        Directory.CreateDirectory(folders.Path("bob"));
        Directory.CreateDirectory(folders.Path("alice/inner"));

        Assert.Equal(SyncStatus.NotADirectory, await StatusOfAsync(mesh, new BindRequest { Directory = "relative/path", PackageId = archive }));
        Assert.Equal(SyncStatus.NotADirectory, await StatusOfAsync(mesh, new BindRequest { Directory = folders.Path("missing"), PackageId = archive }));
        Assert.Equal(SyncStatus.AlreadyBound, await StatusOfAsync(mesh, new BindRequest { Directory = directory + Path.DirectorySeparatorChar, PackageId = archive }));
        Assert.Equal(SyncStatus.NestedBinding, await StatusOfAsync(mesh, new BindRequest { Directory = folders.Path("alice/inner"), PackageId = archive }));
        Assert.Equal(SyncStatus.NotAnArchive, await StatusOfAsync(mesh, new BindRequest { Directory = folders.Path("bob"), PackageId = channel }));
        Assert.Equal(SyncStatus.NotFollowed, await StatusOfAsync(mesh, new BindRequest { Directory = folders.Path("bob"), PackageId = new PackageId(Facts.Id(0x42)) }));
        Assert.Equal(SyncStatus.NotBound, await StatusOfAsync(mesh, new StatusRequest { Directory = folders.Path("bob") }));
        Assert.Equal(SyncStatus.NotBound, await StatusOfAsync(mesh, new ScanRequest { Directory = folders.Path("bob") }));
        Assert.Equal(SyncStatus.NotBound, await StatusOfAsync(mesh, new UnbindRequest { Directory = folders.Path("bob") }));
        Assert.Equal(SyncStatus.NotBound, await StatusOfAsync(mesh, new ConflictsRequest { Directory = folders.Path("bob") }));
        await using LocalClient app = await mesh[Alice].ConnectUserAsync("sync-test", None);
        LocalResult<ReadOnlyMemory<byte>> unknown = await app.SendAsync(new ServiceCall { Service = SyncProtocol.ServiceName, Method = "resolve", Payload = Array.Empty<byte>() }, None);
        LocalResult<ReadOnlyMemory<byte>> garbage = await app.SendAsync(new ServiceCall { Service = SyncProtocol.ServiceName, Method = SyncMethods.Bind, Payload = new byte[] { 1 } }, None);
        LocalResult<BindReply> badPattern = await SyncCall.SendAsync(app, new BindRequest { Directory = folders.Path("bob"), PackageId = archive, Ignore = ["a/b"] }, None);
        Assert.Equal((Status)(ushort)SyncStatus.UnknownMethod, unknown.Status);
        Assert.Equal((Status.Invalid, "payload"), (garbage.Status, garbage.Error!.Detail));
        Assert.Equal((Status.Invalid, "ignore"), (badPattern.Status, badPattern.Error!.Detail));
    }

    // §6: a binding event ends every run, a conflict event reports each conflict recorded; §5.6: conflicts page oldest first.
    [Fact]
    public async Task EventsReportRunsAndConflicts_ConflictsPageOldestFirst()
    {
        await using TestMesh mesh = await StartAsync(Alice, Bob);
        using var folders = new Folders();
        folders.Write("alice/report.txt", "Alice's");
        folders.Write("alice/notes.txt", "Alice's notes");
        PackageId archive = await SharedArchiveAsync(mesh);
        await using (RunningService alice = await RunAsync(mesh, Alice))
        {
            await BindAsync(mesh, Alice, folders.Path("alice"), archive);
            await SettledAsync(mesh, Alice, folders.Path("alice"), b => b.Head != default);
        }

        await mesh.SettleAsync(None);
        folders.Write("bob/report.txt", "Bob's own report");
        folders.Write("bob/notes.txt", "Bob's own notes");
        await using RunningService bob = await RunAsync(mesh, Bob);
        await using LocalClient watcher = await mesh[Bob].ConnectUserAsync("sync-events", None);
        LocalResult<Subscription<ServiceEvent>> subscribed = await watcher.SubscribeAsync<ServiceEvent>(SyncEventReader.Filter, None);
        await using Subscription<ServiceEvent> events = subscribed.Value;

        await BindAsync(mesh, Bob, folders.Path("bob"), archive);
        Binding merged = await SettledAsync(mesh, Bob, folders.Path("bob"), b => b.Runs >= 1 && b.Conflicts == 2);
        (BindingEvent binding, ConflictEvent conflict) = await FirstEventsAsync(events);
        LocalResult<ConflictsReply> first = await CallAsync(mesh, Bob, new ConflictsRequest { Directory = folders.Path("bob"), Limit = 1 });
        LocalResult<ConflictsReply> second = await CallAsync(mesh, Bob, new ConflictsRequest { After = first.Value.Conflicts[0].Id, Limit = 10 });

        Assert.Equal(folders.Path("bob"), binding.Binding.Directory);
        Assert.Equal(ConflictKind.CopyWritten, conflict.Conflict.Kind);
        Assert.Equal((1, true), (first.Value.Conflicts.Count, first.Value.More));
        Assert.Equal((1, false), (second.Value.Conflicts.Count, second.Value.More));
        Assert.True(first.Value.Conflicts[0].Id < second.Value.Conflicts[0].Id);
        Assert.Equal(["notes.txt", "report.txt"], new[] { first.Value.Conflicts[0].Path, second.Value.Conflicts[0].Path }.Order());
        Assert.Equal("Bob's own report", folders.Read("bob/report.txt"));
        Assert.Equal("Alice's", folders.Read($"bob/report.conflict-{DeviceOf(Alice).Value.ToHex()[..8]}.txt"));
        Assert.Equal(2u, merged.Conflicts);
    }

    private static async Task<SyncStatus> StatusOfAsync<TRequest, TReply>(TestMesh mesh, ISyncMethod<TRequest, TReply> request)
        where TRequest : class, ISyncMethod<TRequest, TReply>
        where TReply : class, IWireRecord<TReply>
    {
        LocalResult<TReply> result = await CallAsync(mesh, Alice, request);
        return SyncStatusText.IsSyncStatus(result.Status, out SyncStatus status) ? status : SyncStatus.None;
    }

    private static async Task<(BindingEvent Binding, ConflictEvent Conflict)> FirstEventsAsync(Subscription<ServiceEvent> events)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        BindingEvent? binding = null;
        ConflictEvent? conflict = null;
        await foreach (ServiceEvent received in events.WithCancellation(deadline.Token))
        {
            if (SyncEventReader.TryRead(received, out SyncEvent? read))
            {
                binding ??= read as BindingEvent;
                conflict ??= read as ConflictEvent;
            }

            if (binding is not null && conflict is not null)
                return (binding, conflict);
        }

        throw new InvalidOperationException("The events ended before a binding and a conflict event came.");
    }
}
