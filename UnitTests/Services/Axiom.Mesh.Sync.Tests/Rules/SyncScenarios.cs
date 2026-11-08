using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;
using Axiom.Mesh.Testing;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// The sync service's scenario suite (plan §9.4 item 5, SYNC-SERVICE.md §11): a folder bound to an Archive on Alice's laptop and
/// Bob's desktop, edited on either, partitioned, crashed, restarted, read-only, unbound, moved to another store and branched.
/// Written once against <see cref="IMeshFixture"/> with the sync service running in process beside each node, it runs unchanged
/// in memory (<c>InMemorySyncScenarios</c>) and on real sockets with the real pipe (<c>SocketSyncScenarios</c> in the
/// integration suite, which compiles this very file). Runs are started with <c>scan</c> and awaited on the binding's status
/// (no run pending, the runs counted), never on time; the watcher's debounce is proved apart, on the fake clock.
/// </summary>
public abstract class SyncScenarios
{
    private const string Alice = "alice-laptop";
    private const string Bob = "bob-desktop";
    private const string Phone = "alice-phone";

    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>Starts a mesh of the named nodes whose local ports list the sync service beside <c>cli</c>.</summary>
    /// <param name="nodes">The nodes, by published device name.</param>
    /// <returns>The running mesh; the scenario disposes it.</returns>
    protected abstract Task<IMeshFixture> StartAsync(IReadOnlyList<string> nodes);

    /// <summary>How many small files and how many MiB in one file scenario 6 syncs: 1 000 and 8 in memory.</summary>
    protected virtual (int SmallFiles, int LargeMiB) Volume => (1_000, 8);

    /// <summary>How long a scenario waits for one step at most; longer on sockets for scenario 6.</summary>
    protected virtual TimeSpan StepDeadline => TimeSpan.FromSeconds(30);

    // How long one status call may take: the Host answers a call its service does not within 60 s (LOCAL-SPEC.md §9.8).
    private static TimeSpan CallDeadline { get; } = TimeSpan.FromSeconds(75);

    // 1. Alice's folder appears byte-equal in Bob's empty bound directory.
    [Fact]
    public async Task AlicesFolderAppearsByteEqualInBobsEmptyBoundDirectory()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        folders.Write("alice/report.txt", "the report");
        folders.Write("alice/docs/deep/notes.md", "notes");
        folders.Write("alice/empty/", string.Empty);
        PackageId archive = await SharedArchiveAsync(mesh);
        await using RunningService aliceSync = await RunSyncAsync(mesh, Alice);
        await using RunningService bobSync = await RunSyncAsync(mesh, Bob);

        await BindAsync(mesh, Alice, folders.Path("alice"), archive);
        await SettledAsync(mesh, Alice, folders.Path("alice"), b => b.Head != default);
        await mesh.SettleAsync(None);
        await BindAsync(mesh, Bob, folders.Path("bob"), archive);

        await SettledAsync(mesh, Bob, folders.Path("bob"), _ => folders.Same("alice", "bob"));
        Assert.True(Directory.Exists(folders.Path("bob/empty")));
        Assert.True(File.Exists(Path.Combine(folders.Path("bob"), SyncProtocol.MarkerName)));
    }

    // 2. An edit on Bob reaches Alice, and both hold one leaf: Bob's Version.
    [Fact]
    public async Task AnEditOnBobReachesAlice_OneLeafOnBoth()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        (PackageId archive, RunningService aliceSync, RunningService bobSync) = await SyncedPairAsync(mesh, folders, ("a.txt", "one"));
        await using (aliceSync)
        await using (bobSync)
        {
            folders.Write("bob/a.txt", "two, from Bob");
            await ScanAsync(mesh, Bob, folders.Path("bob"));
            await mesh.SettleAsync(None);

            Binding atAlice = await SettledAsync(mesh, Alice, folders.Path("alice"), _ => folders.Read("alice/a.txt") == "two, from Bob");
            Binding atBob = await StatusAsync(mesh, Bob, folders.Path("bob"));
            Assert.Equal((1, 1), (atAlice.Leaves, atBob.Leaves));
            Assert.Equal(atBob.Head, atAlice.Head);
        }
    }

    // 3. Edits to different files while the two are apart merge without a conflict: one merge Version with two parents, the
    // same on both, and both folders equal.
    [Fact]
    public async Task EditsToDifferentFilesWhileApartMergeWithoutConflict_OneMergeVersionWithTwoParents()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        (PackageId archive, RunningService aliceSync, RunningService bobSync) = await SyncedPairAsync(mesh, folders, ("a.txt", "a"), ("b.txt", "b"));
        await ApartAsync(mesh, folders, aliceSync, bobSync, ("alice/a.txt", "a from Alice"), ("bob/b.txt", "b from Bob"));

        await using RunningService aliceAgain = await RunSyncAsync(mesh, Alice);
        await SettledAsync(mesh, Alice, folders.Path("alice"), b => b.Leaves == 1 && folders.Read("alice/b.txt") == "b from Bob");
        await mesh.SettleAsync(None);
        await using RunningService bobAgain = await RunSyncAsync(mesh, Bob);
        await SettledAsync(mesh, Bob, folders.Path("bob"), _ => folders.Same("alice", "bob"));

        Assert.Equal("a from Alice", folders.Read("bob/a.txt"));
        foreach (string node in new[] { Alice, Bob })
        {
            IReadOnlyList<(Entry Entry, VersionPayload Version)> versions = await VersionsAsync(mesh, node, archive);
            Assert.Single(versions, v => v.Entry.Parents.Count == 2);
        }

        Assert.Empty(Directory.GetFiles(folders.Path("alice"), "*.conflict-*"));
    }

    // 4. The same file edited on both while apart: the leaf later in the total order wins, the other side's file lies beside it as
    // <name>.conflict-<8 hex of its device>, identically on both.
    [Fact]
    public async Task TheSameFileEditedOnBoth_TheLaterLeafWins_TheOtherLiesBeside_IdenticallyOnBoth()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        (PackageId archive, RunningService aliceSync, RunningService bobSync) = await SyncedPairAsync(mesh, folders, ("report.txt", "v1"));
        await ApartAsync(mesh, folders, aliceSync, bobSync, ("alice/report.txt", "Alice's v2"), ("bob/report.txt", "Bob's v2"));

        await using RunningService aliceAgain = await RunSyncAsync(mesh, Alice);
        await SettledAsync(mesh, Alice, folders.Path("alice"), b => b.Leaves == 1 && Directory.GetFiles(folders.Path("alice"), "report.conflict-*").Length == 1);
        await mesh.SettleAsync(None);
        await using RunningService bobAgain = await RunSyncAsync(mesh, Bob);
        await SettledAsync(mesh, Bob, folders.Path("bob"), _ => folders.Same("alice", "bob"));

        IReadOnlyList<(Entry Entry, VersionPayload Version)> versions = await VersionsAsync(mesh, Alice, archive);
        Entry fromAlice = versions.Single(v => v.Entry.DeviceId == DeviceOf(Alice) && v.Entry.Parents.Count == 1 && v.Entry.Clock > 0 && versions.Any(o => o.Entry.Hash == v.Entry.Parents[0])).Entry;
        Entry fromBob = versions.Single(v => v.Entry.DeviceId == DeviceOf(Bob)).Entry;
        bool aliceLater = new EntryKey(fromAlice.Clock, fromAlice.DeviceId, fromAlice.Hash) > new EntryKey(fromBob.Clock, fromBob.DeviceId, fromBob.Hash);
        (string winner, DeviceId loser, string loserText) = aliceLater ? ("Alice's v2", DeviceOf(Bob), "Bob's v2") : ("Bob's v2", DeviceOf(Alice), "Alice's v2");
        foreach (string side in new[] { "alice", "bob" })
        {
            Assert.Equal(winner, folders.Read($"{side}/report.txt"));
            Assert.Equal(loserText, folders.Read($"{side}/report.conflict-{loser.Value.ToHex()[..8]}.txt"));
        }
    }

    // 5. A deletion reaches the other side, and so does a directory replaced by a file, and a file replaced by a directory.
    [Fact]
    public async Task ADeletionPropagates_AndADirectoryBecomesAFileAndBackBothWays()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        (PackageId archive, RunningService aliceSync, RunningService bobSync) = await SyncedPairAsync(mesh, folders, ("gone.txt", "g"), ("d/inner.txt", "i"), ("f", "a file"));
        await using (aliceSync)
        await using (bobSync)
        {
            folders.Delete("alice/gone.txt");
            folders.Delete("alice/d");
            folders.Write("alice/d", "now a file");
            await ScanAsync(mesh, Alice, folders.Path("alice"));
            await mesh.SettleAsync(None);
            await SettledAsync(mesh, Bob, folders.Path("bob"), _ => folders.Same("alice", "bob"));

            folders.Delete("bob/f");
            folders.Write("bob/f/inner.txt", "now a directory");
            await ScanAsync(mesh, Bob, folders.Path("bob"));
            await mesh.SettleAsync(None);
            await SettledAsync(mesh, Alice, folders.Path("alice"), _ => folders.Same("alice", "bob"));

            Assert.False(File.Exists(folders.Path("bob/gone.txt")));
            Assert.Equal("now a file", folders.Read("bob/d"));
            Assert.Equal("now a directory", folders.Read("alice/f/inner.txt"));
        }
    }

    // 6. Many small files and one large one reach the other side byte-equal (1 000 and 8 MiB in memory; 10 000 and 64 MiB on
    // sockets, under the release floor).
    [Fact]
    public async Task ManySmallFilesAndALargeOneArriveByteEqual()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        (int small, int largeMiB) = Volume;
        for (int i = 0; i < small; i++)
            folders.Write($"alice/batch-{i / 500:D3}/file-{i:D5}.txt", $"file {i} " + new string((char)('a' + (i % 26)), i % 300));
        folders.WriteBytes("alice/large.bin", largeMiB * 1_048_576);
        PackageId archive = await SharedArchiveAsync(mesh);
        await using RunningService aliceSync = await RunSyncAsync(mesh, Alice);
        await using RunningService bobSync = await RunSyncAsync(mesh, Bob);

        await BindAsync(mesh, Alice, folders.Path("alice"), archive);
        await SettledAsync(mesh, Alice, folders.Path("alice"), b => b.Head != default);
        await mesh.SettleAsync(None);
        await BindAsync(mesh, Bob, folders.Path("bob"), archive);
        await SettledAsync(mesh, Bob, folders.Path("bob"), b => b.Files == (ulong)(small + 1), connected: (Alice, Bob));

        Assert.True(folders.Same("alice", "bob"));
    }

    // 7. A service stopped while it writes the branch into the directory (as soon as its first file is there) resumes when it
    // runs again: no second Version, no partial file, the directory complete. CrashResumeTests stop runs at each exact step.
    [Fact]
    public async Task AServiceStoppedMidSyncResumes_NoDuplicateVersionNoPartialFile()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        for (int i = 0; i < 200; i++)
            folders.Write($"alice/f{i:D3}.txt", $"content {i} " + new string('c', 4_000));
        PackageId archive = await SharedArchiveAsync(mesh);
        await using RunningService aliceSync = await RunSyncAsync(mesh, Alice);
        await BindAsync(mesh, Alice, folders.Path("alice"), archive);
        await SettledAsync(mesh, Alice, folders.Path("alice"), b => b.Head != default);
        await mesh.SettleAsync(None);

        RunningService stopped = await RunSyncAsync(mesh, Bob);
        await BindAsync(mesh, Bob, folders.Path("bob"), archive);
        await FirstFileAsync(mesh, Bob, folders.Path("bob"), "f*.txt");
        await stopped.DisposeAsync();
        await using RunningService resumed = await RunSyncAsync(mesh, Bob);
        await SettledAsync(mesh, Bob, folders.Path("bob"), _ => folders.Same("alice", "bob"));

        Assert.Empty(Directory.GetFiles(folders.Path("bob"), StoreFiles.PartialPrefix + "*", SearchOption.AllDirectories));
        Assert.DoesNotContain(await VersionsAsync(mesh, Bob, archive), v => v.Entry.DeviceId == DeviceOf(Bob));
    }

    // 8. A sync service that stopped while Alice went on writing catches up when it runs again: it ends at her latest Version.
    [Fact]
    public async Task AServiceThatStoppedCatchesUpWhenItRunsAgain()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        (PackageId archive, RunningService aliceSync, RunningService bobSync) = await SyncedPairAsync(mesh, folders, ("a.txt", "0"));
        await using (aliceSync)
        {
            await bobSync.DisposeAsync();
            for (int i = 1; i <= 3; i++)
            {
                folders.Write("alice/a.txt", $"edit {i}");
                folders.Write($"alice/new-{i}.txt", $"new {i}");
                await ScanAsync(mesh, Alice, folders.Path("alice"));
            }

            await mesh.SettleAsync(None);
            await using RunningService bobAgain = await RunSyncAsync(mesh, Bob);
            await SettledAsync(mesh, Bob, folders.Path("bob"), _ => folders.Same("alice", "bob"));

            Assert.Equal("edit 3", folders.Read("bob/a.txt"));
        }
    }

    // 9. A follower who may only read pulls every change and never publishes; its own edits stay and are reported.
    [Fact]
    public async Task AReadOnlyFollowerPullsAndNeverPublishes_ItsEditsAreReported()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        folders.Write("alice/a.txt", "a");
        folders.Write("alice/b.txt", "b");
        PackageId archive = await SharedArchiveAsync(mesh, GrantRights.Read);
        await using RunningService aliceSync = await RunSyncAsync(mesh, Alice);
        await using RunningService bobSync = await RunSyncAsync(mesh, Bob);
        await BindAsync(mesh, Alice, folders.Path("alice"), archive);
        await SettledAsync(mesh, Alice, folders.Path("alice"), b => b.Head != default);
        await mesh.SettleAsync(None);
        await BindAsync(mesh, Bob, folders.Path("bob"), archive);
        await SettledAsync(mesh, Bob, folders.Path("bob"), _ => folders.Same("alice", "bob"));

        folders.Write("bob/a.txt", "Bob's edit he may not publish");
        Binding reported = await ScanAsync(mesh, Bob, folders.Path("bob"));
        folders.Write("alice/b.txt", "b from Alice");
        await ScanAsync(mesh, Alice, folders.Path("alice"));
        await mesh.SettleAsync(None);
        Binding pulled = await SettledAsync(mesh, Bob, folders.Path("bob"), _ => folders.Read("bob/b.txt") == "b from Alice");

        Assert.Equal((BindingState.LocalChangesReadOnly, GrantRights.Read), (reported.State, reported.Rights));
        Assert.True(reported.LocalChanges >= 1);
        Assert.Equal("Bob's edit he may not publish", folders.Read("bob/a.txt"));
        Assert.Equal(BindingState.LocalChangesReadOnly, pulled.State);
        Assert.DoesNotContain(await VersionsAsync(mesh, Alice, archive), v => v.Entry.DeviceId == DeviceOf(Bob));
    }

    // 10. Unbinding leaves every file where it is; binding the directory again merges what it holds as local changes.
    [Fact]
    public async Task UnbindLeavesTheFiles_RebindingMergesThemAsLocalChanges()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        (PackageId archive, RunningService aliceSync, RunningService bobSync) = await SyncedPairAsync(mesh, folders, ("a.txt", "a"));
        await using (aliceSync)
        await using (bobSync)
        {
            await using (LocalClient app = await mesh.ConnectUserAsync(Bob, "sync-scenario", None))
                Ok(await SyncCall.SendAsync(app, new UnbindRequest { Directory = folders.Path("bob") }, None), "unbind");
            Assert.Equal("a", folders.Read("bob/a.txt"));
            Assert.False(File.Exists(Path.Combine(folders.Path("bob"), SyncProtocol.MarkerName)));

            folders.Write("bob/added while unbound.txt", "kept");
            await BindAsync(mesh, Bob, folders.Path("bob"), archive);
            await SettledAsync(mesh, Bob, folders.Path("bob"), b => b.Runs >= 1);
            await mesh.SettleAsync(None);
            await ScanAsync(mesh, Alice, folders.Path("alice"));

            await SettledAsync(mesh, Alice, folders.Path("alice"), _ => folders.Same("alice", "bob"));
            Assert.Equal("kept", folders.Read("alice/added while unbound.txt"));
        }
    }

    // 11. A node that was offline catches up with what was written meanwhile, and a node with nothing changed writes no Version.
    [Fact]
    public async Task ANodeOfflineForAWhileCatchesUp_NoVersionWrittenWhenNothingChanged()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        (PackageId archive, RunningService aliceSync, RunningService bobSync) = await SyncedPairAsync(mesh, folders, ("a.txt", "a"));
        await using (aliceSync)
        {
            int before = (await VersionsAsync(mesh, Alice, archive)).Count;
            await bobSync.DisposeAsync();
            await mesh.StopNodeAsync(Bob, None);
            folders.Write("alice/a.txt", "while Bob was away");
            await ScanAsync(mesh, Alice, folders.Path("alice"));
            folders.Write("alice/b.txt", "also while Bob was away");
            await ScanAsync(mesh, Alice, folders.Path("alice"));
            await mesh.StartNodeAsync(Bob, None);
            await mesh.ConnectAsync(Alice, Bob, None);
            await mesh.SettleAsync(None);
            await using RunningService bobAgain = await RunSyncAsync(mesh, Bob);
            await SettledAsync(mesh, Bob, folders.Path("bob"), _ => folders.Same("alice", "bob"));
            await ScanAsync(mesh, Bob, folders.Path("bob"));
            await mesh.SettleAsync(None);

            IReadOnlyList<(Entry Entry, VersionPayload Version)> after = await VersionsAsync(mesh, Bob, archive);
            Assert.Equal(before + 2, after.Count);
            Assert.DoesNotContain(after, v => v.Entry.DeviceId == DeviceOf(Bob));
        }
    }

    // 12. Three leaves written apart (Alice's laptop and phone, Bob's desktop) resolve by a chain of two-parent merges, the
    // same Tree on all three. Bob and the phone never meet: what one writes reaches the other through the laptop.
    [Fact]
    public async Task ThreeConcurrentLeavesResolveByAChainOfTwoParentMerges_IdenticallyEverywhere()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob, Phone]);
        using var folders = new Folders();
        folders.Write("alice/shared.txt", "s");
        PackageId archive = await SharedArchiveAsync(mesh);
        await mesh.ConnectAsync(Alice, Phone, None);
        await using (LocalClient phone = await mesh.ConnectUserAsync(Phone, "sync-scenario", None))
            Ok(await phone.SendAsync(new FollowPackage { PackageId = archive }, None), "FollowPackage");
        await mesh.SettleAsync(None);
        var services = new Dictionary<string, RunningService>();
        foreach ((string node, string side) in new[] { (Alice, "alice"), (Bob, "bob"), (Phone, "phone") })
        {
            services[node] = await RunSyncAsync(mesh, node);
            await BindAsync(mesh, node, folders.Path(side), archive);
            await SettledAsync(mesh, node, folders.Path(side), b => b.Head != default && folders.Same("alice", side));
            await mesh.SettleAsync(None);
        }

        // Apart: each writes its own file and publishes, the others stopped.
        foreach ((string node, string side) in new[] { (Alice, "alice"), (Bob, "bob"), (Phone, "phone") })
            await services[node].DisposeAsync();
        foreach (string node in new[] { Alice, Bob, Phone })
            await mesh.StopNodeAsync(node, None);
        foreach ((string node, string side) in new[] { (Alice, "alice"), (Bob, "bob"), (Phone, "phone") })
        {
            await mesh.StartNodeAsync(node, None);
            folders.Write($"{side}/{side}.txt", $"written on {side}");
            await using (RunningService alone = await RunSyncAsync(mesh, node))
                await ScanAsync(mesh, node, folders.Path(side));
            await mesh.StopNodeAsync(node, None);
        }

        foreach (string node in new[] { Alice, Bob, Phone })
            await mesh.StartNodeAsync(node, None);
        await mesh.ConnectAsync(Alice, Bob, None);
        await mesh.ConnectAsync(Alice, Phone, None);
        await mesh.SettleAsync(None);
        await using RunningService merger = await RunSyncAsync(mesh, Alice);
        await SettledAsync(mesh, Alice, folders.Path("alice"), b => b.Leaves == 1 && File.Exists(folders.Path("alice/bob.txt")) && File.Exists(folders.Path("alice/phone.txt")));
        await mesh.SettleAsync(None);
        await using RunningService bobAgain = await RunSyncAsync(mesh, Bob);
        await using RunningService phoneAgain = await RunSyncAsync(mesh, Phone);
        await SettledAsync(mesh, Bob, folders.Path("bob"), _ => folders.Same("alice", "bob"));
        await SettledAsync(mesh, Phone, folders.Path("phone"), _ => folders.Same("alice", "phone"));

        IReadOnlyList<(Entry Entry, VersionPayload Version)> versions = await VersionsAsync(mesh, Alice, archive);
        Assert.Equal(2, versions.Count(v => v.Entry.Parents.Count == 2));
        Assert.All(versions, v => Assert.True(v.Entry.Parents.Count <= 2));
    }

    // 13. A service whose database meets another store epoch starts its binding over as a first sync: the Version DAG is gone,
    // the directory merges with the branch of the Host it serves now, and no local edit is lost.
    [Fact]
    public async Task AnotherStoreEpochMakesTheBindingAFirstSync_NoLocalEditLost()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        (PackageId archive, RunningService aliceSync, RunningService bobSync) = await SyncedPairAsync(mesh, folders, ("a.txt", "a"), ("b.txt", "b"));
        await aliceSync.DisposeAsync();
        await bobSync.DisposeAsync();
        folders.Write("bob/b.txt", "Bob's edit before the move");

        await using RunningService moved = await mesh.RunServiceAsync(Alice, new SyncService(Options(mesh)), Bob, None);
        Binding first = await SettledAsync(mesh, Alice, folders.Path("bob"), b => b.Runs >= 1 && b.State == BindingState.InSync && b.Leaves == 1);

        Assert.Equal("Bob's edit before the move", folders.Read("bob/b.txt"));
        Assert.Equal("a", folders.Read("bob/a.txt"));
        IReadOnlyList<(Entry Entry, VersionPayload Version)> versions = await VersionsAsync(mesh, Alice, archive);
        Assert.Equal(first.Head, versions.Single(v => v.Entry.Hash == first.Head).Entry.Hash);
        Assert.Equal(DeviceOf(Alice), versions.Single(v => v.Entry.Hash == first.Head).Entry.DeviceId);
    }

    // 14. A Version of another branch (beta) leaves a binding of main alone.
    [Fact]
    public async Task AVersionOfAnotherBranchIsIgnoredByAMainBinding()
    {
        await using IMeshFixture mesh = await StartAsync([Alice, Bob]);
        using var folders = new Folders();
        (PackageId archive, RunningService aliceSync, RunningService bobSync) = await SyncedPairAsync(mesh, folders, ("a.txt", "main"));
        await using (aliceSync)
        await using (bobSync)
        {
            folders.Write("beta/a.txt", "beta");
            folders.Write("beta/only-in-beta.txt", "beta only");
            await BindAsync(mesh, Alice, folders.Path("beta"), archive, "beta");
            await SettledAsync(mesh, Alice, folders.Path("beta"), b => b.Head != default);
            await mesh.SettleAsync(None);
            await ScanAsync(mesh, Bob, folders.Path("bob"));

            Assert.Equal("main", folders.Read("bob/a.txt"));
            Assert.False(File.Exists(folders.Path("bob/only-in-beta.txt")));
            Assert.Contains(await VersionsAsync(mesh, Bob, archive), v => v.Version.Label == "beta");
        }
    }

    private static DeviceId DeviceOf(string node) => TestIdentity.Of(node).DeviceId;

    private static UserId UserOf(string node) => TestIdentity.Of(node).UserId;

    private static T Ok<T>(LocalResult<T> result, string what)
    {
        Assert.True(result.IsOk, $"{what} was refused with {result.Status}: {result.Error?.Message}");
        return result.Value;
    }

    private static SyncServiceOptions Options(IMeshFixture mesh) => new() { Time = mesh.Clock, Stability = TimeSpan.Zero };

    private static Task<RunningService> RunSyncAsync(IMeshFixture mesh, string node) => mesh.RunServiceAsync(node, new SyncService(Options(mesh)), None);

    // Alice creates the Archive and grants Bob; Bob follows it once Alice's listing reached him.
    private static async Task<PackageId> SharedArchiveAsync(IMeshFixture mesh, GrantRights rights = GrantRights.Write)
    {
        await mesh.PairAsync(Alice, Bob, None);
        await mesh.ConnectAsync(Alice, Bob, None);
        await mesh.HoldUserKeyAsync(Alice, None);
        PackageId archive;
        await using (LocalClient owner = await mesh.ConnectUserAsync(Alice, "sync-scenario", None))
        {
            archive = Ok(await owner.SendAsync(new CreatePackage { Kind = PackageKind.Archive, Name = "Shared" }, None), "CreatePackage").PackageId;
            Ok(await owner.SendAsync(new SetGrant { PackageId = archive, GranteeId = UserOf(Bob), Rights = rights }, None), "SetGrant");
        }

        await mesh.SettleAsync(None);
        await using (LocalClient follower = await mesh.ConnectUserAsync(Bob, "sync-scenario", None))
            Ok(await follower.SendAsync(new FollowPackage { PackageId = archive }, None), "FollowPackage");
        await mesh.SettleAsync(None);
        return archive;
    }

    // An Archive bound on both, Alice's files on both.
    private async Task<(PackageId Archive, RunningService AliceSync, RunningService BobSync)> SyncedPairAsync(IMeshFixture mesh, Folders folders, params (string Path, string Text)[] files)
    {
        foreach ((string path, string text) in files)
            folders.Write("alice/" + path, text);
        PackageId archive = await SharedArchiveAsync(mesh);
        RunningService aliceSync = await RunSyncAsync(mesh, Alice);
        RunningService bobSync = await RunSyncAsync(mesh, Bob);
        await BindAsync(mesh, Alice, folders.Path("alice"), archive);
        await SettledAsync(mesh, Alice, folders.Path("alice"), b => b.Head != default);
        await mesh.SettleAsync(None);
        await BindAsync(mesh, Bob, folders.Path("bob"), archive);
        await SettledAsync(mesh, Bob, folders.Path("bob"), _ => folders.Same("alice", "bob"));
        return (archive, aliceSync, bobSync);
    }

    // Both stopped; each edits and publishes alone while the other node is down; both nodes up and connected again, services stopped.
    private async Task ApartAsync(IMeshFixture mesh, Folders folders, RunningService aliceSync, RunningService bobSync, (string Path, string Text) onAlice, (string Path, string Text) onBob)
    {
        await aliceSync.DisposeAsync();
        await bobSync.DisposeAsync();
        await mesh.StopNodeAsync(Bob, None);
        folders.Write(onAlice.Path, onAlice.Text);
        await using (RunningService alone = await RunSyncAsync(mesh, Alice))
            await ScanAsync(mesh, Alice, folders.Path("alice"));
        await mesh.StopNodeAsync(Alice, None);
        await mesh.StartNodeAsync(Bob, None);
        folders.Write(onBob.Path, onBob.Text);
        await using (RunningService alone = await RunSyncAsync(mesh, Bob))
            await ScanAsync(mesh, Bob, folders.Path("bob"));
        await mesh.StartNodeAsync(Alice, None);
        await mesh.ConnectAsync(Alice, Bob, None);
        await mesh.SettleAsync(None);
    }

    private static async Task<Binding> BindAsync(IMeshFixture mesh, string node, string directory, PackageId archive, string label = SyncProtocol.DefaultLabel)
    {
        await using LocalClient app = await mesh.ConnectUserAsync(node, "sync-scenario", None);
        return Ok(await SyncCall.SendAsync(app, new BindRequest { Directory = directory, PackageId = archive, Label = label }, None), $"bind on {node}").Binding;
    }

    private static async Task<Binding> StatusAsync(IMeshFixture mesh, string node, string directory)
    {
        await using LocalClient app = await mesh.ConnectUserAsync(node, "sync-scenario", None);
        return Ok(await SyncCall.SendAsync(app, new StatusRequest { Directory = directory }, None), $"status on {node}").Bindings.Single();
    }

    // Queues a run and waits until it ended.
    private async Task<Binding> ScanAsync(IMeshFixture mesh, string node, string directory)
    {
        ulong before = (await StatusAsync(mesh, node, directory)).Runs;
        await using (LocalClient app = await mesh.ConnectUserAsync(node, "sync-scenario", None))
            Ok(await SyncCall.SendAsync(app, new ScanRequest { Directory = directory }, None), $"scan on {node}");
        return await SettledAsync(mesh, node, directory, b => b.Runs > before);
    }

    // Asks the service until no run is pending and the condition holds, within the step's deadline. Each look is a connection
    // of its own and bounded by itself: one connection held through a long run was seen wedged in its write under the full
    // suite's load (STRUCTURAL-DEBT.md 78), and a step must report where the binding stood, not a cancelled write.
    // With connected, the two nodes are kept established meanwhile (the run's Fetch needs the other as its source): under the
    // full suite's load a session can end and come back only after its backoff, which is not what the step is about.
    private async Task<Binding> SettledAsync(IMeshFixture mesh, string node, string directory, Func<Binding, bool> condition, (string A, string B)? connected = null)
    {
        using var deadline = new CancellationTokenSource(StepDeadline);
        while (true)
        {
            LocalResult<StatusReply> status;
            using (var call = new CancellationTokenSource(CallDeadline))
            await using (LocalClient app = await mesh.ConnectUserAsync(node, "sync-scenario", None))
            {
                try
                {
                    status = await SyncCall.SendAsync(app, new StatusRequest { Directory = directory }, call.Token);
                }
                catch (OperationCanceledException) when (call.IsCancellationRequested)
                {
                    throw new TestMeshException($"A status call to the sync service on {node} did not return within {CallDeadline.TotalSeconds:0} s.", "Check that the Host reads the connection and the service is connected.");
                }
            }

            if (status.IsOk && status.Value.Bindings is [{ Pending: false } binding] && condition(binding))
                return binding;
            if (deadline.IsCancellationRequested)
                throw new TestMeshException($"The binding of {directory} on {node} did not settle as expected within {StepDeadline.TotalSeconds:0} s ({Folders.LastDifference}; status: {(status.IsOk ? status.Value.Bindings.SingleOrDefault() : status)}).", "Check that the node stores the Archive's Versions and that the service is connected.");
            if (connected is { } pair)
                await mesh.ConnectAsync(pair.A, pair.B, None);
            await Pause.ShortAsync();
        }
    }

    // The moment a pull has written its first file into the directory, within the step deadline; a run that wrote none by
    // then is reported with the binding's state (the Linux unit job saw this wait run out under full-suite load).
    private async Task FirstFileAsync(IMeshFixture mesh, string node, string directory, string pattern)
    {
        using var deadline = new CancellationTokenSource(StepDeadline);
        while (Directory.GetFiles(directory, pattern).Length == 0)
        {
            if (deadline.IsCancellationRequested)
            {
                string state;
                await using (LocalClient app = await mesh.ConnectUserAsync(node, "sync-scenario", None))
                {
                    LocalResult<StatusReply> status = await SyncCall.SendAsync(app, new StatusRequest { Directory = directory }, None);
                    state = status.IsOk ? string.Join(" | ", status.Value.Bindings) : status.ToString();
                }

                throw new TestMeshException(
                    $"No file matching {pattern} appeared in {directory} on {node} within {StepDeadline.TotalSeconds:0} s; the directory holds {Directory.EnumerateFileSystemEntries(directory).Count()} entries; binding: {state}.",
                    "Read the binding's state: WaitingForObjects names a Fetch no peer answered, Scanning or Pulling a run still in hand, InSync with no files a run that planned nothing.");
            }

            await Pause.ShortAsync();
        }
    }

    private static async Task<IReadOnlyList<(Entry Entry, VersionPayload Version)>> VersionsAsync(IMeshFixture mesh, string node, PackageId archive)
    {
        await using LocalClient app = await mesh.ConnectUserAsync(node, "sync-scenario", None);
        var versions = new List<(Entry, VersionPayload)>();
        EntryKey? after = null;
        while (true)
        {
            EntryPage page = Ok(await app.SendAsync(new ListHistory { PackageId = archive, After = after, Limit = 64 }, None), "ListHistory");
            foreach (Entry entry in page.Entries)
            {
                if (EntryPayload.TryParse(entry.Kind, entry.Payload, out EntryPayload? payload) && payload is VersionPayload version)
                    versions.Add((entry, version));
            }

            if (!page.More || page.Entries.Count == 0)
                return versions;
            Entry last = page.Entries[^1];
            after = new EntryKey(last.Clock, last.DeviceId, last.Hash);
        }
    }

    // The scenario's folders, deleted at the end: one per side, compared by their files' bytes, the reserved names left out.
    private sealed class Folders : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "axiom-mesh-sync-" + Guid.NewGuid().ToString("N")[..12]);

        public Folders()
        {
            foreach (string side in new[] { "alice", "bob", "phone", "beta" })
                Directory.CreateDirectory(System.IO.Path.Combine(root, side));
        }

        public static string LastDifference { get; private set; } = string.Empty;

        public string Path(string relative) => System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

        public void Write(string relative, string text)
        {
            string path = Path(relative);
            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(path);
                return;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public void WriteBytes(string relative, int size)
        {
            string path = Path(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            byte[] content = new byte[size];
            for (int i = 0; i < size; i++)
                content[i] = (byte)((i * 31) + (i >> 13));
            File.WriteAllBytes(path, content);
        }

        public string Read(string relative) => File.Exists(Path(relative)) ? File.ReadAllText(Path(relative)) : "(absent)";

        public void Delete(string relative)
        {
            string path = Path(relative);
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }

        // Whether two sides hold the same files with the same bytes and the same directories.
        public bool Same(string left, string right)
        {
            try
            {
                List<string> ofLeft = [.. Snapshot(left)];
                List<string> ofRight = [.. Snapshot(right)];
                LastDifference = $"only in {left}: {string.Join(", ", ofLeft.Except(ofRight))}; only in {right}: {string.Join(", ", ofRight.Except(ofLeft))}";
                return ofLeft.SequenceEqual(ofRight);
            }
            catch (IOException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file still held by a closing handle; the system's temporary folder is cleaned eventually.
            }
        }

        private IEnumerable<string> Snapshot(string side)
        {
            string folder = Path(side);
            return Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
                .Select(p => (Relative: System.IO.Path.GetRelativePath(folder, p).Replace('\\', '/'), Full: p))
                .Where(p => !p.Relative.Split('/').Any(n => n.StartsWith(FileNameRule.ReservedPrefix, StringComparison.Ordinal)))
                .OrderBy(p => p.Relative, StringComparer.Ordinal)
                .Select(p => Directory.Exists(p.Full) ? p.Relative + "/" : p.Relative + " " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p.Full))))
                .ToList();
        }
    }
}
