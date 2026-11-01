using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Sync;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Integration.Tests.Support;

/// <summary>
/// The sync service's release floors measured on real nodes over loopback TLS and the real pipe, the service in process beside
/// each node on the system's clock (plan §9.4 item 6): a folder of many small files and one large one from Alice's bind to the
/// last byte in Bob's folder; a scan of an unchanged folder of many files; a changed 1 MiB file from the end of the debounce to
/// its Version. The floor tests and the benchmark scenario <c>sync</c> run the same code.
/// </summary>
internal static class SyncMeasure
{
    private const string Alice = "alice-laptop";
    private const string Bob = "bob-desktop";
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>Alice binds a folder of <paramref name="smallFiles"/> files and one of <paramref name="largeMiB"/> MiB; Bob binds an empty one: the time until Bob's holds every byte.</summary>
    /// <param name="smallFiles">How many small files.</param>
    /// <param name="largeMiB">The large file's MiB.</param>
    /// <returns>How long it took.</returns>
    public static async Task<TimeSpan> MeasureFolderAsync(int smallFiles, int largeMiB)
    {
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync([Alice, Bob], [], [], [SyncProgram.Definition], None).ConfigureAwait(false);
        PackageId archive = await SharedArchiveAsync(mesh).ConfigureAwait(false);
        string root = Folder("sync-folder");
        string alice = Path.Combine(root, "alice");
        string bob = Path.Combine(root, "bob");
        Directory.CreateDirectory(bob);
        for (int i = 0; i < smallFiles; i++)
            Write(Path.Combine(alice, $"batch-{i / 1_000:D3}", $"file-{i:D6}.txt"), $"file {i} {new string('x', i % 200)}");
        byte[] large = new byte[largeMiB * 1_048_576];
        for (int i = 0; i < large.Length; i++)
            large[i] = (byte)((i * 31) + (i >> 13));
        await File.WriteAllBytesAsync(Path.Combine(alice, "large.bin"), large).ConfigureAwait(false);

        // A folder that exists before it is bound: older than the stability window, so the first run reads every file
        // (one written a moment ago is busy and waits for a later run, which is a rule of its own, tested by rule).
        DateTime old = DateTime.UtcNow.AddMinutes(-1);
        foreach (string file in Directory.EnumerateFiles(alice, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(file, old);
        try
        {
            await using RunningService aliceSync = await mesh.RunServiceAsync(Alice, new SyncService(), None).ConfigureAwait(false);
            await using RunningService bobSync = await mesh.RunServiceAsync(Bob, new SyncService(), None).ConfigureAwait(false);
            var watch = Stopwatch.StartNew();
            await BindAsync(mesh, Alice, alice, archive).ConfigureAwait(false);
            await UntilAsync(mesh, Alice, alice, b => b.Head != default).ConfigureAwait(false);
            await BindAsync(mesh, Bob, bob, archive).ConfigureAwait(false);
            await UntilAsync(mesh, Bob, bob, b => b.Files == (ulong)(smallFiles + 1) && b.State == BindingState.InSync).ConfigureAwait(false);
            watch.Stop();
            if (!File.ReadAllBytes(Path.Combine(bob, "large.bin")).AsSpan().SequenceEqual(large))
                throw new InvalidOperationException("The large file did not arrive byte-equal.");
            return watch.Elapsed;
        }
        finally
        {
            Delete(root);
        }
    }

    /// <summary>A folder of <paramref name="files"/> files, bound and published once; then a scan of it unchanged: the time the scan's run took.</summary>
    /// <param name="files">How many files.</param>
    /// <returns>How long the unchanged run took.</returns>
    public static async Task<TimeSpan> MeasureUnchangedScanAsync(int files)
    {
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync([Alice], [], [], [SyncProgram.Definition], None).ConfigureAwait(false);
        await mesh.HoldUserKeyAsync(Alice, None).ConfigureAwait(false);
        PackageId archive = await CreateAsync(mesh).ConfigureAwait(false);
        string root = Folder("sync-scan");
        for (int i = 0; i < files; i++)
            Write(Path.Combine(root, $"dir-{i / 1_000:D3}", $"f{i:D6}"), "x");
        try
        {
            // Older than the racy window, so the fingerprints of the first run are trusted by the second (as they would be).
            DateTime old = DateTime.UtcNow.AddMinutes(-1);
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(file, old);
            await using RunningService sync = await mesh.RunServiceAsync(Alice, new SyncService(), None).ConfigureAwait(false);
            await BindAsync(mesh, Alice, root, archive).ConfigureAwait(false);
            Binding first = await UntilAsync(mesh, Alice, root, b => b.Head != default && b.Files == (ulong)files).ConfigureAwait(false);
            await using LocalClient app = await mesh.ConnectUserAsync(Alice, "bench", None).ConfigureAwait(false);
            var watch = Stopwatch.StartNew();
            await SyncCall.SendAsync(app, new ScanRequest { Directory = root }, None).ConfigureAwait(false);
            Binding second = await UntilAsync(mesh, Alice, root, b => b.Runs > first.Runs).ConfigureAwait(false);
            watch.Stop();
            if (second.Head != first.Head)
                throw new InvalidOperationException("The unchanged folder was published again.");
            return watch.Elapsed;
        }
        finally
        {
            Delete(root);
        }
    }

    /// <summary>A bound folder whose 1 MiB file changes once: the time from the end of the 2 s debounce to the Version stored.</summary>
    /// <returns>How long after the debounce the Version was there.</returns>
    public static async Task<TimeSpan> MeasureChangeToVersionAsync()
    {
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync([Alice], [], [], [SyncProgram.Definition], None).ConfigureAwait(false);
        await mesh.HoldUserKeyAsync(Alice, None).ConfigureAwait(false);
        PackageId archive = await CreateAsync(mesh).ConfigureAwait(false);
        string root = Folder("sync-change");
        string file = Path.Combine(root, "photo.bin");
        byte[] bytes = new byte[1_048_576];
        Write(file, "first");
        try
        {
            await using RunningService sync = await mesh.RunServiceAsync(Alice, new SyncService(), None).ConfigureAwait(false);
            await BindAsync(mesh, Alice, root, archive).ConfigureAwait(false);
            Binding first = await UntilAsync(mesh, Alice, root, b => b.Head != default).ConfigureAwait(false);
            new Random(7).NextBytes(bytes);
            await File.WriteAllBytesAsync(file, bytes).ConfigureAwait(false);
            var watch = Stopwatch.StartNew();
            await UntilAsync(mesh, Alice, root, b => b.Head != first.Head).ConfigureAwait(false);
            watch.Stop();
            return watch.Elapsed - SyncLimits.DebounceQuiet;
        }
        finally
        {
            Delete(root);
        }
    }

    private static string Folder(string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "axiom-mesh-bench", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void Delete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A handle still closing; the temporary folder is cleaned eventually.
        }
    }

    private static async Task<PackageId> CreateAsync(RealMeshFixture mesh)
    {
        await using LocalClient owner = await mesh.ConnectUserAsync(Alice, "bench", None).ConfigureAwait(false);
        return (await owner.SendAsync(new CreatePackage { Kind = PackageKind.Archive, Name = "Bench" }, None).ConfigureAwait(false)).Value.PackageId;
    }

    private static async Task<PackageId> SharedArchiveAsync(RealMeshFixture mesh)
    {
        await mesh.PairAsync(Alice, Bob, None).ConfigureAwait(false);
        await mesh.ConnectAsync(Alice, Bob, None).ConfigureAwait(false);
        await mesh.HoldUserKeyAsync(Alice, None).ConfigureAwait(false);
        PackageId archive;
        await using (LocalClient owner = await mesh.ConnectUserAsync(Alice, "bench", None).ConfigureAwait(false))
        {
            archive = (await owner.SendAsync(new CreatePackage { Kind = PackageKind.Archive, Name = "Shared" }, None).ConfigureAwait(false)).Value.PackageId;
            await owner.SendAsync(new SetGrant { PackageId = archive, GranteeId = TestIdentity.Of(Bob).UserId, Rights = GrantRights.Write }, None).ConfigureAwait(false);
        }

        await mesh.SettleAsync(None).ConfigureAwait(false);
        await using (LocalClient follower = await mesh.ConnectUserAsync(Bob, "bench", None).ConfigureAwait(false))
            await follower.SendAsync(new FollowPackage { PackageId = archive }, None).ConfigureAwait(false);
        await mesh.SettleAsync(None).ConfigureAwait(false);
        return archive;
    }

    private static async Task BindAsync(RealMeshFixture mesh, string node, string directory, PackageId archive)
    {
        await using LocalClient app = await mesh.ConnectUserAsync(node, "bench", None).ConfigureAwait(false);
        LocalResult<BindReply> bound = await SyncCall.SendAsync(app, new BindRequest { Directory = directory, PackageId = archive }, None).ConfigureAwait(false);
        if (!bound.IsOk)
            throw new InvalidOperationException("bind: " + bound);
    }

    private static async Task<Binding> UntilAsync(RealMeshFixture mesh, string node, string directory, Func<Binding, bool> condition)
    {
        await using LocalClient app = await mesh.ConnectUserAsync(node, "bench", None).ConfigureAwait(false);
        var watch = Stopwatch.StartNew();
        while (true)
        {
            LocalResult<StatusReply> status = await SyncCall.SendAsync(app, new StatusRequest { Directory = directory }, None).ConfigureAwait(false);
            if (status.IsOk && status.Value.Bindings is [{ Pending: false } binding] && condition(binding))
                return binding;
            // A guard against a hang, not a floor: publishing a hundred thousand files first takes minutes (two puts a file).
            if (watch.Elapsed > TimeSpan.FromMinutes(20))
                throw new TimeoutException($"The sync service did not get there within twenty minutes ({status}).");
            await Task.Yield();
        }
    }
}
