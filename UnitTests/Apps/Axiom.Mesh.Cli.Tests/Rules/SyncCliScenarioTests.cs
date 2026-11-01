using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Cli.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Sync;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// The sync commands end to end against an in-memory node with the sync service beside it (plan §9.4 item 4): a folder added
/// with a new Archive is published, listed, scanned and removed with its files kept; publishing to an Archive while the service
/// holds the kind points at <c>sync add</c>; with the service gone, a sync command says it is not connected.
/// </summary>
public sealed class SyncCliScenarioTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task SyncAdd_Status_Scan_Conflicts_Remove_ThroughTheSyncService_AndPublishPointsAtSyncAdd()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop") with { Local = TestServices.With(TestCliService.Local, SyncProgram.Definition) }, None);
        TestNode alice = mesh["alice-laptop"];
        await ((IMeshFixture)mesh).HoldUserKeyAsync("alice-laptop", None);
        RunningService sync = await alice.RunServiceAsync(new SyncService(new SyncServiceOptions { Time = mesh.Clock, Stability = TimeSpan.Zero }), None);
        string folder = Path.Combine(Path.GetTempPath(), "axiom-mesh-cli-sync-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Path.Combine(folder, "shared"));
        File.WriteAllText(Path.Combine(folder, "shared", "a.txt"), "synced");
        try
        {
            CliResult added = await CliMeshes.OkAsync(alice.CliAsync($"sync add '{Path.Combine(folder, "shared")}' --create Shared --ignore '*.tmp'", None));
            await SettledAsync(alice, Path.Combine(folder, "shared"));
            CliResult status = await CliMeshes.OkAsync(alice.CliAsync($"sync status '{Path.Combine(folder, "shared", "a.txt")}'", None));
            CliResult listed = await CliMeshes.OkAsync(alice.CliAsync("sync list --json", None));
            CliResult scanned = await CliMeshes.OkAsync(alice.CliAsync($"sync scan '{Path.Combine(folder, "shared")}'", None));
            CliResult conflicts = await CliMeshes.OkAsync(alice.CliAsync($"sync conflicts '{Path.Combine(folder, "shared")}'", None));
            CliResult publish = await alice.CliAsync($"package publish '{Path.Combine(folder, "shared")}' --package Shared", None);
            await SettledAsync(alice, Path.Combine(folder, "shared"));
            CliResult removed = await CliMeshes.OkAsync(alice.CliAsync($"sync remove '{Path.Combine(folder, "shared")}'", None));

            Assert.Contains("the first run is queued", added.Stdout, StringComparison.Ordinal);
            Assert.Contains("in sync", status.Stdout, StringComparison.Ordinal);
            Assert.Contains("Files          1, 6 B", status.Stdout, StringComparison.Ordinal);
            Assert.Contains("\"ignore\": [\n      \"*.tmp\"\n    ]", listed.Stdout.ReplaceLineEndings("\n"), StringComparison.Ordinal);
            Assert.Contains("a run is queued for 1 binding", scanned.Stdout, StringComparison.Ordinal);
            Assert.Contains("no conflicts", conflicts.Stdout, StringComparison.Ordinal);
            Assert.Equal(CliExitCode.Refused, publish.ExitCode);
            Assert.Contains("the sync service holds Archives on this Host; use 'axiom-mesh sync add", publish.Stderr, StringComparison.Ordinal);
            Assert.Contains("no longer kept in sync; its files stay", removed.Stdout, StringComparison.Ordinal);
            Assert.Equal("synced", File.ReadAllText(Path.Combine(folder, "shared", "a.txt")));

            await sync.DisposeAsync();
            CliResult gone = await alice.CliAsync("sync list", None);
            Assert.Equal(CliExitCode.Refused, gone.ExitCode);
            Assert.Contains("the sync service is not connected", gone.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            await sync.DisposeAsync();
            Directory.Delete(folder, recursive: true);
        }
    }

    // Asks the service until the binding ran and nothing is pending.
    private static async Task SettledAsync(TestNode node, string directory)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        await using LocalClient app = await node.ConnectUserAsync("sync-cli-test", None);
        while (true)
        {
            LocalResult<StatusReply> status = await SyncCall.SendAsync(app, new StatusRequest { Directory = directory }, None);
            if (status.IsOk && status.Value.Bindings is [{ Pending: false, Runs: > 0 }])
                return;
            deadline.Token.ThrowIfCancellationRequested();
            await Pause.ShortAsync();
        }
    }
}
