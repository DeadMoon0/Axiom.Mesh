using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;
using static Axiom.Mesh.Sync.Tests.Support.SyncMeshes;
using Folders = Axiom.Mesh.Sync.Tests.Support.Folders;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// The invariants of a run (SYNC-SERVICE.md §3), each proved by stopping a run where a crash would and starting a new service on
/// the same state: content-addressed steps repeat harmlessly; a leaf of this device that names the Tree stands for the Version
/// a crash left unrecorded; the target is pending before the directory changes and the base moves after; a resumed pull writes
/// only what is left and never overwrites a file changed meanwhile; nothing is left half-written.
/// </summary>
public sealed class CrashResumeTests
{
    // Published, base not yet committed: the next run finds its own leaf naming the Tree and writes no second Version.
    [Fact]
    public async Task AStopRightAfterPublishingNeverMakesASecondVersion()
    {
        await using TestMesh mesh = await StartAsync(Alice);
        using var folders = new Folders();
        folders.Write("alice/a.txt", "a");
        PackageId archive = await OwnArchiveAsync(mesh, Alice);

        await StopAtAsync(mesh, Alice, RunStep.Published, () => BindAsync(mesh, Alice, folders.Path("alice"), archive));
        await using RunningService again = await RunAsync(mesh, Alice);
        Binding settled = await SettledAsync(mesh, Alice, folders.Path("alice"), b => b.Runs >= 1 && b.Head != default);
        await ScanAsync(mesh, Alice, folders.Path("alice"));

        IReadOnlyList<Entry> versions = await VersionsAsync(mesh, Alice, archive);
        Assert.Single(versions);
        Assert.Equal(versions[0].Hash, settled.Head);
    }

    // The target recorded, nothing written: the next run resumes it, without planning again.
    [Fact]
    public async Task AStopAfterTheTargetWasRecordedResumesIt()
    {
        await using TestMesh mesh = await StartAsync(Alice, Bob);
        using var folders = new Folders();
        PackageId archive = await PublishedByAliceAsync(mesh, folders, 6);

        await StopAtAsync(mesh, Bob, RunStep.PendingRecorded, () => BindAsync(mesh, Bob, folders.Path("bob"), archive));
        Assert.Empty(Directory.GetFiles(folders.Path("bob"), "*.txt"));
        await using RunningService again = await RunAsync(mesh, Bob);

        await SettledAsync(mesh, Bob, folders.Path("bob"), _ => folders.Same("alice", "bob"));
        Assert.DoesNotContain(await VersionsAsync(mesh, Bob, archive), v => v.DeviceId == DeviceOf(Bob));
    }

    // One file written, the rest not: the resumed run writes the rest, leaves no temporary file, and keeps a file the user
    // changed meanwhile as the user left it (the next run publishes it).
    [Fact]
    public async Task AResumedPullWritesOnlyWhatIsLeft_NeverAFileChangedMeanwhile_NoPartialFile()
    {
        await using TestMesh mesh = await StartAsync(Alice, Bob);
        using var folders = new Folders();
        PackageId archive = await PublishedByAliceAsync(mesh, folders, 8);

        await StopAtAsync(mesh, Bob, RunStep.FileWritten, () => BindAsync(mesh, Bob, folders.Path("bob"), archive));
        string written = Directory.GetFiles(folders.Path("bob"), "*.txt").Select(Path.GetFileName).Single()!;
        string untouched = Enumerable.Range(0, 8).Select(i => $"f{i}.txt").First(n => n != written);
        folders.Write("bob/" + untouched, "Bob wrote this while the service was down");
        await using RunningService again = await RunAsync(mesh, Bob);
        Binding resumed = await SettledAsync(mesh, Bob, folders.Path("bob"), b => b.Runs >= 1 && File.Exists(folders.Path("bob/f7.txt")) && File.Exists(folders.Path("bob/f0.txt")));

        Assert.Equal("Bob wrote this while the service was down", folders.Read("bob/" + untouched));
        Assert.Empty(Directory.GetFiles(folders.Path("bob"), StoreFiles.PartialPrefix + "*"));
        Assert.True(resumed.LocalChanges >= 1);
    }

    // The base committed, the run not ended: nothing is repeated, and nothing published.
    [Fact]
    public async Task AStopAfterTheBaseWasCommittedRepeatsNothing()
    {
        await using TestMesh mesh = await StartAsync(Alice, Bob);
        using var folders = new Folders();
        PackageId archive = await PublishedByAliceAsync(mesh, folders, 3);

        await StopAtAsync(mesh, Bob, RunStep.BaseCommitted, () => BindAsync(mesh, Bob, folders.Path("bob"), archive));
        Assert.True(folders.Same("alice", "bob"));
        await using RunningService again = await RunAsync(mesh, Bob);
        await ScanAsync(mesh, Bob, folders.Path("bob"));

        Assert.True(folders.Same("alice", "bob"));
        Assert.Single(await VersionsAsync(mesh, Bob, archive));
    }

    // A first sync stopped right after it wrote its merge Version: the next run takes that Version as the branch and writes no
    // second one.
    [Fact]
    public async Task AStopAfterTheMergeWasWrittenWritesNoSecondMerge()
    {
        await using TestMesh mesh = await StartAsync(Alice, Bob);
        using var folders = new Folders();
        PackageId archive = await PublishedByAliceAsync(mesh, folders, 2);
        folders.Write("bob/only-on-bob.txt", "Bob's own");

        await StopAtAsync(mesh, Bob, RunStep.MergeWritten, () => BindAsync(mesh, Bob, folders.Path("bob"), archive));
        await using RunningService again = await RunAsync(mesh, Bob);
        await SettledAsync(mesh, Bob, folders.Path("bob"), b => b.Runs >= 1 && File.Exists(folders.Path("bob/f0.txt")));
        await ScanAsync(mesh, Bob, folders.Path("bob"));

        Assert.Single(await VersionsAsync(mesh, Bob, archive), v => v.DeviceId == DeviceOf(Bob));
        Assert.Equal("Bob's own", folders.Read("bob/only-on-bob.txt"));
    }

    // Alice publishes a few files; Bob follows the Archive.
    private static async Task<PackageId> PublishedByAliceAsync(TestMesh mesh, Folders folders, int files)
    {
        for (int i = 0; i < files; i++)
            folders.Write($"alice/f{i}.txt", $"file {i}");
        PackageId archive = await SharedArchiveAsync(mesh);
        await using (RunningService alice = await RunAsync(mesh, Alice))
        {
            await BindAsync(mesh, Alice, folders.Path("alice"), archive);
            await SettledAsync(mesh, Alice, folders.Path("alice"), b => b.Head != default);
        }

        await mesh.SettleAsync(default);
        return archive;
    }

    // Runs a service that stops its run at the step, causes the run, and waits until it stopped there; the service is gone after.
    private static async Task StopAtAsync(TestMesh mesh, string node, RunStep step, System.Func<Task> cause)
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RunningService crashing = await RunAsync(mesh, node, Options(mesh) with { CrashAt = s => s == step && stopped.TrySetResult() });
        await cause();
        await stopped.Task.WaitAsync(TestMesh.Deadline);
        await crashing.DisposeAsync();
    }
}
