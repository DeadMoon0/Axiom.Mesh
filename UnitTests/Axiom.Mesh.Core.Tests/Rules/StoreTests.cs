using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>SPEC.md §11.1 (atomic writes) and LOCAL-SPEC.md §9.7 (direct reads, "evict later").</summary>
public sealed class StoreTests
{
    // SPEC.md §11.1: an object is either absent or complete, never partial. In memory, a write that fails part-way stores
    // nothing and the request fails; on disk, a crash's temporary leftover is never found under the hash, a cancelled
    // write leaves nothing, and a finished one holds exactly the bytes. Writing twice is harmless.
    [Fact]
    public async Task AnInterruptedWriteLeavesNoObject_ACompleteOneIsFoundUnderItsHash()
    {
        var faulty = new FaultyObjectFiles(new InMemoryObjectFiles());
        await using (TestStore node = await TestStore.StartAsync(files: faulty))
        {
            Chunk chunk = Samples.Chunk(5);
            faulty.FailWriteOf(chunk.Hash);
            await Assert.ThrowsAsync<MeshStoreException>(() => node.PutAsync(chunk));
            Assert.False(await node.ContainsAsync(chunk.Hash));
            Assert.False(faulty.TryOpenRead(chunk.Hash, out _));
            Assert.Equal(0, node.Writer.Totals.Objects);

            Assert.Equal(AdmitOutcome.Stored, (await node.PutAsync(chunk)).Outcome);
            Assert.True(node.Store.TryOpen(chunk.Hash, out ObjectHandle? handle));
            using (handle)
                Assert.Equal(chunk.Bytes.ToArray(), await ReadAllAsync(handle.Stream));
        }

        using var temp = new TempDirectory();
        var files = new DirectoryObjectFiles(temp.Path, new DeterministicEntropy(1));
        Chunk onDisk = Samples.Chunk(6, 70_000);
        string path = files.PathOf(onDisk.Hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path + ".tmp-0123456789abcdef", onDisk.Bytes[..1000].ToArray()); // a crash mid-write
        Assert.False(files.TryOpenRead(onDisk.Hash, out _));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await files.WriteAtomicAsync(onDisk.Hash, onDisk.Bytes, cancelled.Token));
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!)); // only the crash's leftover

        Assert.True(await files.WriteAtomicAsync(onDisk.Hash, onDisk.Bytes, CancellationToken.None));
        Assert.False(await files.WriteAtomicAsync(onDisk.Hash, onDisk.Bytes, CancellationToken.None));
        Assert.Equal(onDisk.Bytes.ToArray(), await File.ReadAllBytesAsync(path));
    }

    // LOCAL-SPEC.md §9.7: a reader that opened an object with delete sharing keeps its bytes while the Host evicts it; a
    // delete a reader blocks is "evict later", and the object counts as stored until the unlink succeeded.
    [Fact]
    public async Task AnObjectOpenedFromTheStoreDirectoryKeepsItsBytesWhileEvicted_AndTheHostCountsItEvictedOnlyAfterUnlink()
    {
        using var temp = new TempDirectory();
        var directory = new DirectoryObjectFiles(temp.Path, new DeterministicEntropy(2));
        var files = new FaultyObjectFiles(directory);
        await using TestStore node = await TestStore.StartAsync(files: files);
        Chunk read = Samples.Chunk(7, 50_000);
        Chunk blocked = Samples.Chunk(8, 50_000);
        await node.PutAsync(read);
        await node.PutAsync(blocked);

        Assert.True(directory.TryOpenRead(read.Hash, out Stream? reader));
        using (reader)
        {
            Assert.Equal(EvictionOutcome.Evicted, await node.Store.TryEvictAsync(read.Hash, default));
            Assert.False(File.Exists(directory.PathOf(read.Hash)));
            Assert.Equal(read.Bytes.ToArray(), await ReadAllAsync(reader));
        }

        Assert.False(await node.ContainsAsync(read.Hash));
        Assert.False(node.Store.TryOpen(read.Hash, out _));

        // A reader that did not allow deletion: on Windows a real handle, elsewhere (where unlink never fails) injected.
        FileStream? exclusive = OperatingSystem.IsWindows() ? new FileStream(directory.PathOf(blocked.Hash), FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        if (exclusive is null)
            files.Hold(blocked.Hash);
        long objects = node.Writer.Totals.Objects;

        Assert.Equal(EvictionOutcome.Later, await node.Store.TryEvictAsync(blocked.Hash, default));
        Assert.True(await node.ContainsAsync(blocked.Hash));
        Assert.Equal(objects, node.Writer.Totals.Objects);

        exclusive?.Dispose();
        files.Release(blocked.Hash);
        Assert.Equal(EvictionOutcome.Evicted, await node.Store.TryEvictAsync(blocked.Hash, default));
        Assert.False(await node.ContainsAsync(blocked.Hash));
        Assert.Equal(objects - 1, node.Writer.Totals.Objects);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }
}
