using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §8.6, the operation kinds: <c>Fetch</c> waits for a source and fails <c>NotFound</c> after 60 seconds
/// without one, completes once a source supplies the object, and a cancelled Fetch cancels its wants; <c>Materialise</c>
/// writes byte-equal files and leaves no partial file; <c>PutFile</c> stores a file as Chunks and a ChunkList pinned for
/// the service.
/// </summary>
public sealed class OperationKindTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly byte[] Small = ObjectFixtures.Content(3_000, 1);
    private static readonly byte[] Large = ObjectFixtures.Content(Limits.ChunkLength + 500, 2);

    // §6.4, §8.6: with no established peer that could supply it, a Fetch waits; at 60 seconds it fails NotFound, its
    // message naming the hash.
    [Fact]
    public async Task AFetchWithoutASourceWaits_AndFailsNotFoundAfter60Seconds()
    {
        await using TestMesh mesh = await SharedArchiveAsync(connect: false);
        TestNode bob = mesh["bob-desktop"];
        LocalClient client = await bob.ConnectUserAsync("app", None);
        TreeFixture folder = Folder();

        uint id = (await client.SendAsync(new Fetch { PackageId = Archive(mesh), Hash = folder.Root.Hash }, None)).Value;
        long start = mesh.Clock.GetUtcNow().ToUnixTimeMilliseconds();
        Operation waiting = await UntilAsync(bob, id, o => o.Message.StartsWith("waiting for a source", StringComparison.Ordinal), "the Fetch to wait");
        mesh.Clock.Advance(TimeSpan.FromSeconds(Limits.FetchWithoutSourceSeconds - 1));
        await UntilAsync(bob, id, o => (long)o.Updated == start + ((Limits.FetchWithoutSourceSeconds - 1) * 1000L), "the Fetch's look at 59 seconds");
        Assert.Equal(OperationState.Running, (await client.SendAsync(new GetOperation { OperationId = id }, None)).Value.State);
        mesh.Clock.Advance(TimeSpan.FromSeconds(1));
        Operation failed = await UntilAsync(bob, id, o => o.State != OperationState.Running, "the Fetch to end");

        Assert.Equal(OperationState.Running, waiting.State);
        Assert.Equal((OperationState.Failed, Status.NotFound), (failed.State, failed.Status));
        Assert.Contains(folder.Root.Hash.ToString(), failed.Message, StringComparison.Ordinal);
    }

    // §6.4, §8.6: once a peer that lists the Package is established, the Fetch wants from it and ends Done with the
    // objects and bytes fetched; recursive fetches everything below a Tree; pin pins with reason User.
    [Fact]
    public async Task AFetchCompletesOnceASourceAppears_RecursivelyAndPinned()
    {
        await using TestMesh mesh = await SharedArchiveAsync(connect: false);
        TestNode bob = mesh["bob-desktop"];
        LocalClient client = await bob.ConnectUserAsync("app", None);
        TreeFixture folder = Folder();
        uint id = (await client.SendAsync(new Fetch { PackageId = Archive(mesh), Hash = folder.Root.Hash, Recursive = true, Pin = true }, None)).Value;
        await UntilAsync(bob, id, o => o.Message.StartsWith("waiting for a source", StringComparison.Ordinal), "the Fetch to wait");

        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await mesh.WaitUntilAsync("bob-desktop", n => n.Replicating.ListedBy(Archive(mesh)).Count == 1, "Bob to hear Alice list the Archive", None);
        mesh.Clock.Advance(TimeSpan.FromSeconds(1));
        Operation done = await UntilAsync(bob, id, o => o.State != OperationState.Running, "the Fetch to end");

        Assert.Equal(OperationState.Done, done.State);
        FetchResult result = ResultOf<FetchResult>(done);
        int objects = folder.Objects.Select(o => o.Hash).Distinct().Count();
        Assert.Equal((uint)objects, result.Objects);
        Assert.Equal((ulong)folder.Objects.DistinctBy(o => o.Hash).Sum(o => o.Bytes.Length), result.Bytes);
        foreach (StorableObject stored in folder.Objects)
            Assert.True(await bob.Node.ContainsAsync(stored.Hash, None));
        Assert.Contains((await client.SendAsync(new ListPins { Hash = folder.Root.Hash }, None)).Value.Pins, p => p.Reason == Axiom.Mesh.Local.PinReason.User);
    }

    // §8.5: CancelOperation on a waiting Fetch ends it Cancelled, and the node no longer wants what it fetched.
    [Fact]
    public async Task ACancelledFetchEndsCancelled_AndItsWantIsCancelled()
    {
        await using TestMesh mesh = await SharedArchiveAsync(connect: false);
        TestNode bob = mesh["bob-desktop"];
        LocalClient client = await bob.ConnectUserAsync("app", None);
        ObjectHash root = Folder().Root.Hash;
        uint id = (await client.SendAsync(new Fetch { PackageId = Archive(mesh), Hash = root }, None)).Value;
        await UntilAsync(bob, id, o => o.Message.StartsWith("waiting for a source", StringComparison.Ordinal), "the Fetch to wait");
        Assert.True(bob.Node.Replicating.Wants.IsWanted(root));

        Operation cancelled = (await client.SendAsync(new CancelOperation { OperationId = id }, None)).Value;

        Assert.Equal((OperationState.Cancelled, Status.Cancelled), (cancelled.State, cancelled.Status));
        await Waits.UntilAsync(() => !bob.Node.Replicating.Wants.IsWanted(root), "the want to be cancelled");
    }

    // §6.4, SPEC.md §11.4: Materialise writes the Tree as files byte-equal to what was published, counts files and bytes,
    // leaves no partial file and a marker; existing 1 on that path is AlreadyExists, and existing 2 replaces it.
    [Fact]
    public async Task MaterialiseWritesByteEqualFiles_ExistingFailIsAlreadyExists_ReplaceRewrites()
    {
        await using TestMesh mesh = await SharedArchiveAsync(connect: true);
        TestNode bob = mesh["bob-desktop"];
        TreeFixture folder = Folder();
        await mesh.SettleAsync(None);
        LocalClient client = await bob.ConnectUserAsync("app", None);
        using var temp = new TempDirectory();
        string target = Path.Combine(temp.Path, "photos");

        uint id = (await client.SendAsync(new Materialise { PackageId = Archive(mesh), Hash = folder.Root.Hash, Path = target, Existing = ExistingPath.Fail }, None)).Value;
        Operation done = await UntilAsync(bob, id, o => o.State != OperationState.Running, "Materialise to end");

        Assert.Equal(OperationState.Done, done.State);
        MaterialiseResult result = ResultOf<MaterialiseResult>(done);
        Assert.Equal((2u, (ulong)(Small.Length + Large.Length), 0u), (result.Files, result.Bytes, result.Conflicts));
        Assert.Equal(Small, await File.ReadAllBytesAsync(Path.Combine(target, "a.txt")));
        Assert.Equal(Large, await File.ReadAllBytesAsync(Path.Combine(target, "b.bin")));
        Assert.Equal([MaterialiseWork.Marker, "a.txt", "b.bin"], Directory.GetFiles(target).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(done.Total, done.Done);

        uint again = (await client.SendAsync(new Materialise { PackageId = Archive(mesh), Hash = folder.Root.Hash, Path = target, Existing = ExistingPath.Fail }, None)).Value;
        Assert.Equal(Status.AlreadyExists, (await UntilAsync(bob, again, o => o.State != OperationState.Running, "the second Materialise")).Status);
        await File.WriteAllTextAsync(Path.Combine(target, "a.txt"), "changed");
        uint replace = (await client.SendAsync(new Materialise { PackageId = Archive(mesh), Hash = folder.Root.Hash, Path = target, Existing = ExistingPath.Replace }, None)).Value;
        Assert.Equal(OperationState.Done, (await UntilAsync(bob, replace, o => o.State != OperationState.Running, "the replacing Materialise")).State);
        Assert.Equal(Small, await File.ReadAllBytesAsync(Path.Combine(target, "a.txt")));
    }

    // §6.4: existing 2 never deletes a directory this Host did not materialise; a relative path is Invalid; a Materialise
    // cancelled while it waits for its objects writes nothing.
    [Fact]
    public async Task ReplaceRefusesAForeignDirectory_ARelativePathIsInvalid_ACancelledMaterialiseWritesNothing()
    {
        await using TestMesh mesh = await SharedArchiveAsync(connect: false);
        TestNode bob = mesh["bob-desktop"];
        LocalClient client = await bob.ConnectUserAsync("app", None);
        ObjectHash root = Folder().Root.Hash;
        using var temp = new TempDirectory();
        string foreign = Path.Combine(temp.Path, "mine");
        Directory.CreateDirectory(foreign);
        await File.WriteAllTextAsync(Path.Combine(foreign, "keep.txt"), "keep");

        Assert.Equal(Status.Invalid, (await client.SendAsync(new Materialise { PackageId = Archive(mesh), Hash = root, Path = "relative", Existing = ExistingPath.Fail }, None)).Status);
        uint waiting = (await client.SendAsync(new Materialise { PackageId = Archive(mesh), Hash = root, Path = Path.Combine(temp.Path, "out"), Existing = ExistingPath.Fail }, None)).Value;
        await UntilAsync(bob, waiting, o => o.Message.StartsWith("waiting for a source", StringComparison.Ordinal), "Materialise to wait for its objects");
        Assert.Equal(OperationState.Cancelled, (await client.SendAsync(new CancelOperation { OperationId = waiting }, None)).Value.State);

        Assert.False(Directory.Exists(Path.Combine(temp.Path, "out")));
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await mesh.SettleAsync(None);
        uint refused = (await client.SendAsync(new Materialise { PackageId = Archive(mesh), Hash = root, Path = foreign, Existing = ExistingPath.Replace }, None)).Value;
        Operation ended = await UntilAsync(bob, refused, o => o.State != OperationState.Running, "the refused Materialise");
        Assert.Equal((OperationState.Failed, Status.Invalid), (ended.State, ended.Status));
        Assert.True(File.Exists(Path.Combine(foreign, "keep.txt")));
    }

    // §9.5, §8.6: PutFile cuts a file into Chunks of 1 MiB and the rest, stores them and the ChunkList, pins every one for
    // the service, and names the ChunkList; a missing path is NotFound, a directory Invalid.
    [Fact]
    public async Task PutFileStoresChunksAndAChunkListPinnedForTheService_AMissingPathIsNotFound()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient cli = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "b.bin");
        await File.WriteAllBytesAsync(file, Large);
        ChunkedFile expected = Chunker.Cut(Large);

        uint id = (await cli.SendAsync(new PutFile { Path = file }, None)).Value;
        Operation done = await UntilAsync(node, id, o => o.State != OperationState.Running, "PutFile to end", "cli");
        uint missing = (await cli.SendAsync(new PutFile { Path = Path.Combine(temp.Path, "none") }, None)).Value;
        uint directory = (await cli.SendAsync(new PutFile { Path = temp.Path }, None)).Value;

        Assert.Equal(OperationState.Done, done.State);
        PutFileResult result = ResultOf<PutFileResult>(done);
        Assert.Equal((expected.List.Hash, (ulong)Large.Length, 2u), (result.Hash, result.Size, result.Chunks));
        foreach (ObjectHash hash in expected.Chunks.Select(c => c.Hash).Append(expected.List.Hash))
        {
            Pin pin = Assert.Single((await cli.SendAsync(new ListPins { Hash = hash }, None)).Value.Pins);
            Assert.Equal(Axiom.Mesh.Local.PinReason.Service, pin.Reason);
        }

        Assert.Equal(Status.NotFound, (await UntilAsync(node, missing, o => o.State != OperationState.Running, "the missing PutFile", "cli")).Status);
        Assert.Equal(Status.Invalid, (await UntilAsync(node, directory, o => o.State != OperationState.Running, "the directory PutFile", "cli")).Status);
        Assert.Equal(Status.Invalid, (await cli.SendAsync(new PutFile { Path = "relative.bin" }, None)).Status);
    }

    // Alice's laptop owns an Archive granted to Bob, holding one published folder; Bob follows it. With connect, they are
    // connected and Bob holds the Version.
    private static async Task<TestMesh> SharedArchiveAsync(bool connect)
    {
        TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop") with { Trust = TrustSeed.MutuallyKnown("alice-laptop", "bob-desktop"), Silent = ["alice-laptop", "bob-desktop"] }, None);
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        PackageId archive = await alice.CreatePackageAsync("Photos", PackageKind.Archive, Retention.Default, None);
        await alice.GrantAsync(archive, "bob", GrantRights.Read, None);
        ObjectHash version = await alice.PublishAsync(archive, Folder(), None);
        await bob.FollowAsync(archive, None);
        if (connect)
        {
            await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
            await bob.WaitForEntryAsync(version, None);
        }

        return mesh;
    }

    private static PackageId Archive(TestMesh mesh) => mesh["alice-laptop"].Node.Trust.Packages.Single().Id;

    private static TreeFixture Folder() => ObjectFixtures.Folder(("a.txt", Small), ("b.bin", Large));

    // §8.6: an operation's result body is the kind's record.
    private static T ResultOf<T>(Operation operation)
        where T : class, IWireRecord<T>
    {
        var reader = new Axiom.ProtoStream.Codecs.WireReader(operation.Result);
        Assert.True(T.TryRead(ref reader, out T? value));
        return value!;
    }

    private static async Task<Operation> UntilAsync(TestNode node, uint id, Func<Operation, bool> match, string what, string client = "app")
    {
        Operation? seen = null;
        await Waits.UntilAsync(
            () => node.Node.Local.Book.Live().FirstOrDefault(c => c.Hello?.Name == client)?.Operations.Get(id) is { } now && match(seen = now),
            what);
        return seen!;
    }
}
