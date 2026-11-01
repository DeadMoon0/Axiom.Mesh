using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Cli.Tests.Support;
using Axiom.Mesh.Core;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// The command line end to end against in-memory nodes (fundamentals plan §2.7, §8 acceptance in small): an identity and a
/// Channel made and written from the command line, a folder published on one node and materialised byte-equal on another,
/// <c>watch</c> printing what another node wrote, and two users paired with <c>pair show</c>, <c>pair join</c> and
/// <c>pair confirm</c>.
/// </summary>
public sealed class CliScenarioTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task IdentityCreate_PackageCreate_WriteText_ThenHistoryShowsTheEntry()
    {
        await using TestMesh mesh = await CliMeshes.StartAsync(TestMeshOptions.Of("alice-laptop") with { WithoutIdentity = ["alice-laptop"] });
        TestNode alice = mesh["alice-laptop"];

        CliResult none = await alice.CliAsync("write Chat --text hello", None);
        Assert.Equal(CliExitCode.Refused, none.ExitCode);
        Assert.StartsWith("axiom-mesh: this device has no identity yet", none.Stderr, StringComparison.Ordinal);

        CliResult created = await CliMeshes.OkAsync(alice.CliAsync("identity create --user alice --device laptop", None));
        Assert.Equal(alice.Device.Value.ToHex(), CliMeshes.Value(created, "Device"));
        string package = CliMeshes.Value(await CliMeshes.OkAsync(alice.CliAsync("package create Chat", None)), "Package");
        CliResult written = await CliMeshes.OkAsync(alice.CliAsync("write Chat --text 'hello mesh'", None));
        string entry = Regex.Match(written.Stdout, "entry ([0-9a-f]{64})").Groups[1].Value;

        CliResult history = await CliMeshes.OkAsync(alice.CliAsync("history Chat", None));
        Assert.Contains(entry[..Formats.ShortId], history.Stdout, StringComparison.Ordinal);
        Assert.Contains("message", history.Stdout, StringComparison.Ordinal);
        Assert.Contains("hello mesh", history.Stdout, StringComparison.Ordinal);

        // Ids by prefix: the Package by six hex digits, the Entry by its short form.
        CliResult shown = await CliMeshes.OkAsync(alice.CliAsync("package show " + package[..6], None));
        Assert.Contains("channel", shown.Stdout, StringComparison.Ordinal);
        CliResult one = await CliMeshes.OkAsync(alice.CliAsync("entry " + entry[..Formats.ShortId], None));
        Assert.Equal(entry, CliMeshes.Value(one, "Entry"));
        CliResult status = await CliMeshes.OkAsync(alice.CliAsync("status", None));
        Assert.Contains("user alice", status.Stdout, StringComparison.Ordinal);
    }

    // LOCAL-SPEC.md §5.12, §6.7: services lists every service the Host configures, how it proves its name and whether it is
    // connected; --json for scripts (the install job reads it).
    [Fact]
    public async Task ServicesListsTheConfiguredServices_WithProvenanceAndConnection()
    {
        await using TestMesh mesh = await CliMeshes.StartAsync(TestMeshOptions.Of("alice-laptop"));
        TestNode alice = mesh["alice-laptop"];

        CliResult text = await CliMeshes.OkAsync(alice.CliAsync("services", None));
        await using (LocalClient cli = await alice.ConnectServiceAsync("cli", CliMeshes.Token, None))
        {
            CliResult json = await CliMeshes.OkAsync(alice.CliAsync("services --json", None));
            Assert.Contains("\"connected\": true", json.Stdout, StringComparison.Ordinal);
            Assert.Contains("\"provenance\": \"external\"", json.Stdout, StringComparison.Ordinal);
        }

        Assert.Matches(@"cli\s+external\s+no\s", text.Stdout);
    }

    [Fact]
    public async Task APublishedFolderMaterialisesByteEqualOnASecondNodeThatFollowsIt()
    {
        await using TestMesh mesh = await CliMeshes.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop") with
        {
            Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop"),
            Silent = ["alice-laptop", "bob-desktop"],
        });
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        string folder = Directory.CreateTempSubdirectory("axiom-mesh-publish-").FullName;
        string target = Path.Combine(Directory.CreateTempSubdirectory("axiom-mesh-materialise-").FullName, "photos");
        try
        {
            File.WriteAllText(Path.Combine(folder, "a.txt"), "hello");
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllBytes(Path.Combine(folder, "sub", "b.bin"), [.. Enumerable.Range(0, (3 << 20) + 17).Select(i => (byte)(i * 31))]);
            File.WriteAllBytes(Path.Combine(folder, "empty.dat"), []);

            await alice.HoldUserKeyAsync(None);
            string package = CliMeshes.Value(await CliMeshes.OkAsync(alice.CliAsync("package create Photos --kind archive", None)), "Package");
            await CliMeshes.OkAsync(alice.CliAsync($"grant set Photos {bob.User.Value.ToHex()[..8]}", None));
            CliResult published = await CliMeshes.OkAsync(alice.CliAsync($"package publish '{folder}' --package Photos", None));
            Assert.Equal("3", CliMeshes.Value(published, "Published"));
            var version = new ObjectHash(Hash32.TryParseHex(CliMeshes.Value(published, "Entry"), out Hash32 hash) ? hash : default);

            await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
            await mesh.SettleAsync(None);
            await CliMeshes.OkAsync(bob.CliAsync("package follow " + package, None));
            await bob.WaitForEntryAsync(version, None);
            await mesh.SettleAsync(None);

            CliResult materialised = await CliMeshes.OkAsync(bob.CliAsync($"package materialise {package[..8]} '{target}'", None));

            Assert.Contains("3 files", materialised.Stdout, StringComparison.Ordinal);
            foreach (string relative in new[] { "a.txt", Path.Combine("sub", "b.bin"), "empty.dat" })
                Assert.Equal(File.ReadAllBytes(Path.Combine(folder, relative)), File.ReadAllBytes(Path.Combine(target, relative)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(Path.GetDirectoryName(target)!, recursive: true);
        }
    }

    [Fact]
    public async Task WatchPrintsTheEntryAnotherNodeStores_UntilCtrlC()
    {
        await using TestMesh mesh = await CliMeshes.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop") with
        {
            Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop"),
            Silent = ["alice-laptop", "bob-desktop"],
        });
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        await alice.HoldUserKeyAsync(None);
        string package = CliMeshes.Value(await CliMeshes.OkAsync(alice.CliAsync("package create Chat", None)), "Package");
        await CliMeshes.OkAsync(alice.CliAsync($"grant set Chat {bob.User.Value.ToHex()[..8]}", None));
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await mesh.SettleAsync(None);
        await CliMeshes.OkAsync(bob.CliAsync("package follow " + package, None));
        await mesh.SettleAsync(None);

        var lines = new LineSignal("hello from alice");
        using var ctrlC = new CancellationTokenSource();
        long before = bob.Node.Facts.LastSequence;
        Task<CliResult> watching = bob.CliAsync("watch " + package[..8], lines, ctrlC.Token);
        await mesh.WaitForFactAsync("bob-desktop", f => f.Kind == NodeFactKind.LocalAdmitted && f.Detail.Contains("axiom-mesh", StringComparison.Ordinal), before, "the watch connected", None);

        await CliMeshes.OkAsync(alice.CliAsync("write Chat --text 'hello from alice'", None));
        await lines.Seen;
        ctrlC.Cancel();
        CliResult watched = await watching;

        Assert.Equal(CliExitCode.Ok, watched.ExitCode);
        Assert.Contains("message  hello from alice", watched.Stdout, StringComparison.Ordinal);
        Assert.StartsWith(WatchCommand.Started, watched.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PairShowJoinAndConfirmPairTwoUsersFromTheCommandLine()
    {
        await using TestMesh mesh = await CliMeshes.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop"));
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        LocalClient watcher = await alice.ConnectUserAsync("test", None);

        Task<CliResult> showing = alice.CliAsync("pair show", None);
        Pairing waiting = await mesh.WaitForPairingAsync("alice-laptop", watcher, p => p.Code.Length != 0, "the code", None);
        CliResult joined = await CliMeshes.OkAsync(bob.CliAsync("pair join " + waiting.Code, None));
        CliResult shown = await CliMeshes.OkAsync(showing);

        Assert.StartsWith(waiting.Code + "\n", shown.Stdout, StringComparison.Ordinal);
        string sas = CliMeshes.Value(shown, "Compare");
        Assert.Matches("^[A-Z2-7]{4}-[A-Z2-7]{4}$", sas);
        Assert.Equal(sas, CliMeshes.Value(joined, "Compare"));
        Assert.Contains("pair confirm", joined.Stderr, StringComparison.Ordinal);

        Task<CliResult> aliceConfirms = alice.CliAsync("pair confirm", None);
        await mesh.WaitForPairingAsync("alice-laptop", watcher, p => p.State != PairingState.Confirm, "alice to confirm", None);
        CliResult bobConfirmed = await CliMeshes.OkAsync(bob.CliAsync("pair confirm", None));
        CliResult aliceConfirmed = await CliMeshes.OkAsync(aliceConfirms);

        Assert.Equal("paired", CliMeshes.Value(bobConfirmed, "State"));
        Assert.Equal("paired", CliMeshes.Value(aliceConfirmed, "State"));
        CliResult aliceKnows = await CliMeshes.OkAsync(alice.CliAsync("users", None));
        CliResult bobKnows = await CliMeshes.OkAsync(bob.CliAsync("users", None));
        Assert.Contains(bob.User.Value.ToHex()[..Formats.ShortId], aliceKnows.Stdout, StringComparison.Ordinal);
        Assert.Contains(alice.User.Value.ToHex()[..Formats.ShortId], bobKnows.Stdout, StringComparison.Ordinal);
    }
}
