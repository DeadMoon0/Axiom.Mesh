using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;
using LocalStoredObject = Axiom.Mesh.Local.StoredObject;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §9.5 <c>PutObject</c> and §9.7 <c>GetStorePath</c> and direct reads: an unsigned object is stored and
/// pinned for the service, a signed or malformed one is <c>Invalid</c>; the store directory is named with layout 1, and an
/// object a reader holds open keeps its bytes while it is evicted, counted evicted only once its name is gone.
/// </summary>
public sealed class ServiceObjectTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §9.5: PutObject names the object by its hash, stores it (stored false the second time) and pins it for the service;
    // a signed object and a malformed one are Invalid.
    [Fact]
    public async Task PutObjectStoresAndPinsAnUnsignedObject_ASignedOrMalformedOneIsInvalid()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient cli = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        Chunk chunk = Samples.Chunk(8);

        LocalStoredObject first = (await cli.SendAsync(new PutObject { Object = chunk.Bytes }, None)).Value;
        LocalStoredObject second = (await cli.SendAsync(new PutObject { Object = chunk.Bytes }, None)).Value;

        Assert.Equal((chunk.Hash, true), (first.Hash, first.Stored));
        Assert.Equal((chunk.Hash, false), (second.Hash, second.Stored));
        Assert.Equal(Axiom.Mesh.Local.PinReason.Service, Assert.Single((await cli.SendAsync(new ListPins { Hash = chunk.Hash }, None)).Value.Pins).Reason);
        Assert.Equal(Status.Invalid, (await cli.SendAsync(new PutObject { Object = TestIdentity.RosterOf("alice").Bytes }, None)).Status);
        Assert.Equal(Status.Invalid, (await cli.SendAsync(new PutObject { Object = new byte[] { (byte)ObjectTag.Tree, 1, 0, 1 } }, None)).Status);
        Assert.Equal(Status.NotPermitted, (await (await node.ConnectUserAsync("app", None)).SendAsync(new PutObject { Object = chunk.Bytes }, None)).Status);
    }

    // §9.7, §14.4: GetStorePath names the store directory with layout 1 when Host capability bit 0 is set; without a
    // store directory on disk it is NotPermitted.
    [Fact]
    public async Task GetStorePathNamesTheDirectoryWithLayout1_ElseNotPermitted()
    {
        using var temp = new TempDirectory();
        await using TestMesh mesh = await OnDiskAsync(temp.Path);
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient cli = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);

        StorePath path = (await cli.SendAsync(new GetStorePath(), None)).Value;

        Assert.Equal((Path.GetFullPath(temp.Path), (byte)1), (path.Path, path.Layout));
        Assert.True(cli.Host.Capabilities.HasFlag(HostCapabilities.StoreReadable));
        await using TestMesh inMemory = await LocalMeshes.StartAsync();
        LocalClient elsewhere = await inMemory[LocalMeshes.Node].ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        Assert.Equal(Status.NotPermitted, (await elsewhere.SendAsync(new GetStorePath(), None)).Status);
        Assert.False(elsewhere.Host.Capabilities.HasFlag(HostCapabilities.StoreReadable));
    }

    // §9.7: a reader that opened an object by name, allowing deletion, keeps its bytes while the object is evicted; the
    // Host counts it evicted exactly when its name is gone. A reader that does not allow deletion (Windows) makes it
    // "evict later": still stored and counted until the reader closes and the unlink succeeds. Real directory, both OSes.
    [Fact]
    public async Task AnObjectOpenedFromTheStoreDirectoryKeepsItsBytesWhileEvicted_AndTheHostCountsItEvictedOnlyAfterUnlink()
    {
        using var temp = new TempDirectory();
        await using TestMesh mesh = await OnDiskAsync(temp.Path);
        TestNode node = mesh[LocalMeshes.Node];
        LocalClient cli = await node.ConnectServiceAsync("cli", LocalMeshes.CliToken, None);
        string store = (await cli.SendAsync(new GetStorePath(), None)).Value.Path;
        Chunk shared = await PutUnpinnedAsync(cli, Samples.Chunk(10, 5000));
        Chunk held = await PutUnpinnedAsync(cli, Samples.Chunk(11, 5000));

        await using (var reader = new FileStream(FileOf(store, shared.Hash), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            EvictionOutcome outcome = await node.Node.Store.TryEvictAsync(shared.Hash, None);
            bool gone = !File.Exists(FileOf(store, shared.Hash));
            Assert.Equal(gone ? EvictionOutcome.Evicted : EvictionOutcome.Later, outcome);
            Assert.Equal(gone, !await node.Node.ContainsAsync(shared.Hash, None));
            var bytes = new MemoryStream();
            await reader.CopyToAsync(bytes);
            Assert.Equal(shared.Bytes.ToArray(), bytes.ToArray());
        }

        long objects = node.Node.Writer.Totals.Objects;
        await using (new FileStream(FileOf(store, held.Hash), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            EvictionOutcome outcome = await node.Node.Store.TryEvictAsync(held.Hash, None);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(EvictionOutcome.Later, outcome);
                Assert.True(File.Exists(FileOf(store, held.Hash)));
                Assert.True(await node.Node.ContainsAsync(held.Hash, None));
                Assert.Equal(objects, node.Node.Writer.Totals.Objects);
            }
            else
            {
                Assert.Equal(EvictionOutcome.Evicted, outcome);
            }
        }

        if (OperatingSystem.IsWindows())
            Assert.Equal(EvictionOutcome.Evicted, await node.Node.Store.TryEvictAsync(held.Hash, None));
        Assert.False(File.Exists(FileOf(store, held.Hash)));
        Assert.False(await node.Node.ContainsAsync(held.Hash, None));
        Assert.Equal(objects - 1, node.Node.Writer.Totals.Objects);
        Assert.Equal(Status.NotFound, (await cli.SendAsync(new GetObject { Hash = held.Hash }, None)).Status);
    }

    // §9.7 layout 1: <path>/objects/<h[0..1]>/<h[2..3]>/<h>.
    private static string FileOf(string store, ObjectHash hash)
    {
        string hex = hash.ToString();
        return Path.Combine(store, "objects", hex[..2], hex[2..4], hex);
    }

    private static async Task<Chunk> PutUnpinnedAsync(LocalClient cli, Chunk chunk)
    {
        Assert.True((await cli.SendAsync(new PutObject { Object = chunk.Bytes }, None)).IsOk);
        Assert.Equal((ushort)1, (await cli.SendAsync(new Unpin { Hashes = [chunk.Hash] }, None)).Value);
        return chunk;
    }

    private static Task<TestMesh> OnDiskAsync(string directory) => TestMesh.StartAsync(
        TestMeshOptions.Of(LocalMeshes.Node) with
        {
            Local = LocalMeshes.Options,
            StoreDirectories = new Dictionary<string, string> { [LocalMeshes.Node] = directory },
        },
        None);
}
