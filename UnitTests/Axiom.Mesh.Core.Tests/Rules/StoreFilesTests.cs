using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// The file helpers of Axiom.Mesh.Local against a running node (LOCAL-SPEC.md draft 4, §9.5, §9.7; SPEC.md §8.2 to §8.4,
/// §11.4): what <c>Trees.PublishDirectoryAsync</c> puts, <c>StoreFiles.MaterialiseAsync</c> writes back byte for byte,
/// from <c>GetObject</c> when the Host's capability bit 0 is clear and from the store directory when a service may read it;
/// a cancelled write leaves no partial file visible; a file that changes while it is read is not stored.
/// </summary>
public sealed class StoreFilesTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly (string Path, byte[] Content)[] Files =
    [
        ("a.bin", ObjectFixtures.Content(2_500_000, 1)),
        ("docs/b.txt", ObjectFixtures.Content(3_000, 2)),
        ("docs/deeper/c.txt", ObjectFixtures.Content(10, 3)),
        ("empty", []),
    ];

    // §9.5, SPEC.md §11.4 with bit 0 clear (an in-memory store): a published folder, written to a Version whose envelope
    // references its root Tree, materialises byte-equal from GetObject, with the marker and no partial file; existing
    // Fail is AlreadyExists, Replace rewrites a materialised directory and refuses a foreign one.
    [Fact]
    public async Task AFolderPublishedWithTheHelpersMaterialisesByteEqualFromGetObject_AndExistingIsHonoured()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        using var temp = new TempDirectory();
        string source = WriteFolder(Path.Combine(temp.Path, "source"));
        LocalClient cli = await ArchiveServiceAsync(mesh);
        Assert.False(cli.Host.Capabilities.HasFlag(HostCapabilities.StoreReadable));
        PackageId archive = await ArchiveAsync(mesh);

        (Entry version, PublishedTree published) = await PublishAsync(cli, archive, source);
        Assert.Equal([published.Root], version.References);
        Assert.Equal(Files.Length, published.Files);

        string target = Path.Combine(temp.Path, "out");
        var reports = new List<MaterialiseProgress>();
        MaterialisedTree written = Ok(await StoreFiles.MaterialiseAsync(cli, version.Hash, target, ExistingPath.Fail, new Recorder<MaterialiseProgress>(reports), None));
        AssertSameFiles(source, target);
        Assert.Equal((published.Root, Files.Length, (ulong)Files.Sum(f => f.Content.Length)), (written.Tree, written.Files, written.Bytes));
        Assert.Empty(written.Conflicts);
        Assert.Equal(written.Bytes, reports[^1].Done);

        Assert.Equal(Status.AlreadyExists, (await StoreFiles.MaterialiseAsync(cli, version.Hash, target, ExistingPath.Fail, null, None)).Status);
        File.WriteAllText(Path.Combine(target, "a.bin"), "changed");
        Assert.True((await StoreFiles.MaterialiseAsync(cli, published.Root, target, ExistingPath.Replace, null, None)).IsOk);
        AssertSameFiles(source, target);
        Assert.Equal(Status.Invalid, (await StoreFiles.MaterialiseAsync(cli, published.Root, source, ExistingPath.Replace, null, None)).Status);
    }

    // §9.7: a service to which the Host reports its store directory readable (bit 0) reads objects straight from the files;
    // the result is the same folder, byte for byte. A user connection reads them with GetObject.
    [Fact]
    public async Task AServiceMayReadTheStoreDirectory_AUserReadsWithGetObject_BothWriteTheSameFolder()
    {
        using var temp = new TempDirectory();
        await using TestMesh mesh = await TestMesh.StartAsync(
            TestMeshOptions.Of(LocalMeshes.Node) with
            {
                Local = LocalMeshes.Options,
                StoreDirectories = new Dictionary<string, string> { [LocalMeshes.Node] = Path.Combine(temp.Path, "store") },
            },
            None);
        string source = WriteFolder(Path.Combine(temp.Path, "source"));
        LocalClient cli = await ArchiveServiceAsync(mesh);
        Assert.True(cli.Host.Capabilities.HasFlag(HostCapabilities.StoreReadable));
        (Entry version, _) = await PublishAsync(cli, await ArchiveAsync(mesh), source);

        Ok(await StoreFiles.MaterialiseAsync(cli, version.Hash, Path.Combine(temp.Path, "by-service"), ExistingPath.Fail, null, None));
        LocalClient user = await mesh[LocalMeshes.Node].ConnectUserAsync("materialiser", None);
        Ok(await StoreFiles.MaterialiseAsync(user, version.Hash, Path.Combine(temp.Path, "by-user"), ExistingPath.Fail, null, None));

        AssertSameFiles(source, Path.Combine(temp.Path, "by-service"));
        AssertSameFiles(source, Path.Combine(temp.Path, "by-user"));
    }

    // SPEC.md §11.4: cancelled while the large file is being written, no partial file is left visible and the file is not
    // there; what was complete before stays.
    [Fact]
    public async Task ACancelledMaterialiseLeavesNoPartialFileVisible()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        using var temp = new TempDirectory();
        LocalClient cli = await ArchiveServiceAsync(mesh);
        (Entry version, _) = await PublishAsync(cli, await ArchiveAsync(mesh), WriteFolder(Path.Combine(temp.Path, "source")));
        string target = Path.Combine(temp.Path, "out");
        using var cancel = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StoreFiles.MaterialiseAsync(cli, version.Hash, target, ExistingPath.Fail, new Recorder<MaterialiseProgress>([], _ => cancel.Cancel()), cancel.Token));

        string[] left = [.. Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Select(Path.GetFileName)!];
        Assert.DoesNotContain(left, name => name!.StartsWith(StoreFiles.PartialPrefix, StringComparison.Ordinal));
        Assert.DoesNotContain("a.bin", left);
    }

    // §9.5: a missing path is NotFound, a directory Invalid; a file that grows while it is read is IoError and gets no
    // ChunkList; an empty file is a ChunkList with no Chunks.
    [Fact]
    public async Task PutFileRefusesAMissingPathADirectoryAndAFileThatChangesWhileItIsRead()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        using var temp = new TempDirectory();
        LocalClient cli = await ArchiveServiceAsync(mesh);
        string growing = Path.Combine(temp.Path, "growing.bin");
        File.WriteAllBytes(growing, ObjectFixtures.Content(Limits.ChunkLength + 100, 9));
        string empty = Path.Combine(temp.Path, "empty");
        File.WriteAllBytes(empty, []);

        Assert.Equal(Status.NotFound, (await StoreFiles.PutFileAsync(cli, Path.Combine(temp.Path, "none"), null, None)).Status);
        Assert.Equal(Status.Invalid, (await StoreFiles.PutFileAsync(cli, temp.Path, null, None)).Status);
        var append = new Recorder<long>([], _ => File.AppendAllText(growing, "more"));
        LocalResult<StoredFile> changed = await StoreFiles.PutFileAsync(cli, growing, append, None);
        Assert.Equal(Status.IoError, changed.Status);

        StoredFile none = Ok(await StoreFiles.PutFileAsync(cli, empty, null, None));
        Assert.Equal((0ul, 0), (none.Size, none.Chunks.Count));
        Assert.True(await mesh[LocalMeshes.Node].Node.ContainsAsync(none.ChunkList, None));
    }

    // §9.5, SPEC.md §8.2, §8.3: with several Chunks in flight the ChunkList names the Chunks in file order, the same file
    // as with one at a time; a window outside 1 to 64 (LOCAL-SPEC.md §3.4) is the caller's mistake.
    [Fact]
    public async Task APipelinedPutStoresTheSameFileInFileOrderAsOneAtATime()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        using var temp = new TempDirectory();
        LocalClient cli = await ArchiveServiceAsync(mesh);
        byte[] content = DistinctChunks(5, 777, 40);
        string path = Path.Combine(temp.Path, "five.bin");
        File.WriteAllBytes(path, content);

        StoredFile pipelined = Ok(await StoreFiles.PutFileAsync(cli, path, 4, null, None));
        StoredFile alone = Ok(await StoreFiles.PutFileAsync(cli, path, 1, null, None));

        ChunkedFile expected = Chunker.Cut(content);
        Assert.Equal(expected.List.Hash, pipelined.ChunkList);
        Assert.Equal(expected.Chunks.Select(c => c.Hash), pipelined.Chunks);
        Assert.Equal((pipelined.ChunkList, pipelined.Size), (alone.ChunkList, alone.Size));
        foreach (Chunk chunk in expected.Chunks)
            Assert.True(await mesh[LocalMeshes.Node].Node.ContainsAsync(chunk.Hash, None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => StoreFiles.PutFileAsync(cli, path, 0, null, None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => StoreFiles.PutFileAsync(cli, path, Limits.MaxOutstandingRequests + 1, null, None));
    }

    // §6.5, §9.5: a put the Host refuses part-way (the pinned bytes would pass the budget) ends the file with that refusal;
    // no ChunkList is put, and no put is left outstanding on the connection.
    [Fact]
    public async Task APipelinedPutThatTheHostRefusesPartWayEndsWithTheRefusalAndPutsNoChunkList()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        using var temp = new TempDirectory();
        LocalClient cli = await ArchiveServiceAsync(mesh);
        LocalClient user = await mesh[LocalMeshes.Node].ConnectUserAsync("app", None);
        ulong pinned = (await user.SendAsync(new GetStore(), None)).Value.PinnedBytes;
        Assert.True((await user.SendAsync(new SetStoreBudget { Bytes = pinned + (2 * (ulong)Limits.ChunkLength) + 100 }, None)).IsOk);
        byte[] content = DistinctChunks(6, 0, 60);
        string path = Path.Combine(temp.Path, "six.bin");
        File.WriteAllBytes(path, content);

        LocalResult<StoredFile> refused = await StoreFiles.PutFileAsync(cli, path, 3, null, None);

        Assert.Equal(Status.StoreFull, refused.Status);
        Assert.False(await mesh[LocalMeshes.Node].Node.ContainsAsync(Chunker.Cut(content).List.Hash, None));
        Assert.True((await cli.SendAsync(new GetStorePath(), None)).Status is Status.Ok or Status.NotPermitted);
    }

    // Content whose every 1 MiB part differs (ObjectFixtures.Content repeats every 64 KiB, so its Chunks would be one object).
    private static byte[] DistinctChunks(int chunks, int remainder, byte seed) =>
        [.. Enumerable.Range(0, chunks).SelectMany(i => ObjectFixtures.Content(Limits.ChunkLength, (byte)(seed + i))), .. ObjectFixtures.Content(remainder, (byte)(seed + chunks))];

    private static async Task<LocalClient> ArchiveServiceAsync(TestMesh mesh)
    {
        LocalClient cli = await mesh[LocalMeshes.Node].ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        Assert.True((await cli.SendAsync(new RegisterKind { Kind = PackageKind.Archive, EntryKinds = [EntryKind.Version] }, None)).IsOk);
        return cli;
    }

    private static Task<PackageId> ArchiveAsync(TestMesh mesh) =>
        mesh[LocalMeshes.Node].CreatePackageAsync("Photos", PackageKind.Archive, Retention.Default, None);

    // LOCAL-SPEC.md §9.5: put the folder, write the Version that references it, then unpin.
    private static async Task<(Entry Version, PublishedTree Published)> PublishAsync(LocalClient cli, PackageId archive, string folder)
    {
        PublishedTree published = Ok(await Trees.PublishDirectoryAsync(cli, folder, null, None));
        var payload = new VersionPayload { Tree = published.Root, Label = "main", Prerelease = string.Empty, Notes = string.Empty };
        Entry version = Ok(await cli.SendAsync(WriteEntry.Of(archive, payload.ToBody()), None));
        foreach (ObjectHash[] batch in published.Objects.Chunk(1_024))
            Ok(await cli.SendAsync(new Unpin { Hashes = batch }, None));
        return (version, published);
    }

    private static string WriteFolder(string folder)
    {
        foreach ((string path, byte[] content) in Files)
        {
            string file = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, content);
        }

        return folder;
    }

    private static void AssertSameFiles(string expected, string actual)
    {
        string[] names = [.. Files.Select(f => f.Path).Order(StringComparer.Ordinal)];
        string[] written = [.. Directory.EnumerateFiles(actual, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(actual, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => f != StoreFiles.MarkerName)
            .Order(StringComparer.Ordinal)];
        Assert.Equal(names, written);
        Assert.True(File.Exists(Path.Combine(actual, StoreFiles.MarkerName)));
        foreach (string name in names)
            Assert.Equal(File.ReadAllBytes(Path.Combine(expected, name)), File.ReadAllBytes(Path.Combine(actual, name)));
    }

    private static T Ok<T>(LocalResult<T> result)
    {
        Assert.True(result.IsOk, $"{result.Status}: {result.Error?.Message}");
        return result.Value;
    }

    // Reports synchronously, so a test acts at a known point of the work.
    private sealed class Recorder<T>(List<T> reports, Action<T>? act = null) : IProgress<T>
    {
        public void Report(T value)
        {
            reports.Add(value);
            act?.Invoke(value);
        }
    }
}
