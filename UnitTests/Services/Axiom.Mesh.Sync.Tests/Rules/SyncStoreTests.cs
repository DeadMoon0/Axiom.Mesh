using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Local;
using static Axiom.Mesh.Sync.Tests.Support.Facts;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// The service's database (SYNC-SERVICE.md §10) on LDB's in-memory storage: another store epoch drops the Version DAG and every
/// binding's leaves and pending target, and keeps what describes the disk (base Tree, fingerprints); conflicts are numbered in the
/// order recorded; the DAG reloads as it was written.
/// </summary>
public sealed class SyncStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task AnotherStoreEpochDropsTheDagLeavesAndPending_KeepsTheBaseAndFingerprints()
    {
        await using Database database = await Database.OpenAsync(new InMemoryStorageEnvironment(), DatabaseOptions.Default, None);
        SyncStore store = await SyncStore.OpenAsync(database, None);
        byte[] id = BindingIdentity.Of("/home/alice/shared", new PackageId(Id(0xa7)));
        await store.SaveBindingAsync(new BindingRow { Id = id, Directory = "/home/alice/shared", Package = RowBytes.Of(Id(0xa7)), Label = "main", BaseTree = RowBytes.Of(Id(0xb1)), BaseLeaves = RowBytes.Join([Hash(1)]), PendingTree = RowBytes.Of(Id(0xb2)), PendingLeaves = RowBytes.Join([Hash(2)]) }, None);
        await store.WriteFingerprintsAsync(id, [new Fingerprint("a.txt", TreeEntryMode.File, 1, 2, Hash(3), 4)], [], Durability.Full, None);
        await store.AddVersionsAsync([new VersionRow { Hash = RowBytes.Of(Id(1)), Package = RowBytes.Of(Id(0xa7)), Device = RowBytes.Of(Id(0x1a)), Label = "main", Tree = RowBytes.Of(Id(0xb1)) }], 9, None);

        await store.DiscardVersionsAsync(Id(0xee), None);

        BindingRow kept = (await store.BindingsAsync(None)).Single();
        Assert.Equal((Id(0xb1), 0, (byte[]?)null, 0), (RowBytes.Hash(kept.BaseTree), kept.BaseLeaves.Length, kept.PendingTree, kept.PendingLeaves.Length));
        Assert.Equal(Hash(3), (await store.FingerprintsAsync(id, None))["a.txt"].Hash);
        Assert.Empty(await store.VersionsAsync(None));
        Assert.Equal((Id(0xee), 0ul), ((await store.ReadMetaAsync(None)).Epoch, (await store.ReadMetaAsync(None)).LastSeq));
    }

    [Fact]
    public async Task ConflictsAreNumberedInOrder_AndTheDagReloadsAsWritten()
    {
        await using Database database = await Database.OpenAsync(new InMemoryStorageEnvironment(), DatabaseOptions.Default, None);
        SyncStore store = await SyncStore.OpenAsync(database, None);
        byte[] one = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1];
        byte[] two = [2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2];
        await store.AddConflictsAsync([new ConflictRow { Binding = one, Path = "a" }, new ConflictRow { Binding = two, Path = "b" }], None);
        await store.AddConflictsAsync([new ConflictRow { Binding = one, Path = "c" }], None);
        VersionFact root = Version(1, 0, Laptop, tree: Hash(0xb1));
        VersionFact child = Version(2, 1, Desktop, tree: Hash(0xb2), parents: [root]);
        VersionIndex index = await VersionIndex.LoadAsync(store, None);
        await store.AddVersionsAsync([Row(root), Row(child)], 7, None);

        (List<ConflictRow> all, bool more) = await store.ConflictsAsync(null, 0, 10, None);
        (List<ConflictRow> ofOne, _) = await store.ConflictsAsync(one, 1, 10, None);
        VersionIndex reloaded = await VersionIndex.LoadAsync(store, None);

        Assert.Equal(["a", "b", "c"], all.Select(c => c.Path));
        Assert.False(more);
        Assert.Equal(["c"], ofOne.Select(c => c.Path));
        Assert.Equal([1ul, 2ul, 3ul], all.Select(c => c.Id));
        Assert.Equal([child.Hash], Leaves.Of(reloaded.Of(new PackageId(Id(0xa7))), "main").Select(l => l.Hash));
        Assert.Equal(7ul, reloaded.LastSeq);
        Assert.Equal(0ul, index.LastSeq);
    }

    private static VersionRow Row(VersionFact fact) => new()
    {
        Hash = RowBytes.Of(fact.Hash.Value),
        Package = RowBytes.Of(Id(0xa7)),
        Clock = fact.Key.Clock,
        Device = RowBytes.Of(fact.Key.Device.Value),
        Parents = RowBytes.Join([.. fact.Parents]),
        Label = fact.Label,
        Tree = RowBytes.Of(fact.Tree.Value),
    };
}
