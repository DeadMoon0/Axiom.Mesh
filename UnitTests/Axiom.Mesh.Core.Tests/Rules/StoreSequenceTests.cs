using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;
using Axiom.LDB.Storage.InMemory;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>LOCAL-SPEC.md §5.6 (store sequence), §9.3 (store epoch), and the schema the database was made by.</summary>
public sealed class StoreSequenceTests
{
    // LOCAL-SPEC.md §5.6: the store sequence is 1 for the first Entry this Host ever stored, then +1; unique, increasing,
    // never reused, persisted with the Entry; an Entry evicted and stored again gets a new one.
    [Fact]
    public async Task TheFirstEntryStoredIs1ThenPlusOne_AnEvictedEntryStoredAgainGetsANewSeq_AndItSurvivesARestart()
    {
        TestStore node = await TestStore.StartAsync();
        await node.IdentifyAsync(TestKeys.Alice, TestKeys.AliceLaptop);
        await node.PairAsync(TestKeys.Bob);
        await node.PutAsync(Samples.Roster(TestKeys.Bob, 1, (TestKeys.BobDesktop, false)));
        VerifiedManifest channel = Samples.Channel(TestKeys.Bob);
        PackageId id = channel.Content.Package;
        await node.FollowAsync(id);
        await node.PutAsync(channel);
        VerifiedEntry first = Samples.Message(TestKeys.BobDesktop, id, 0, EntryParents.None, "one");
        VerifiedEntry second = Samples.Message(TestKeys.BobDesktop, id, 1, EntryParents.Of(first.Hash), "two");

        Assert.Equal(1UL, (await node.PutFromPeerAsync(Samples.Admit(first, node.Trust), TestKeys.BobDesktop)).StoreSeq);
        Assert.Equal(2UL, (await node.PutFromPeerAsync(Samples.Admit(second, node.Trust), TestKeys.BobDesktop)).StoreSeq);
        await node.UnfollowAsync(id);
        Assert.Equal(EvictionOutcome.Evicted, await node.Store.TryEvictAsync(first.Hash, default));
        await node.FollowAsync(id);
        Assert.Equal(3UL, (await node.PutFromPeerAsync(Samples.Admit(first, node.Trust), TestKeys.BobDesktop)).StoreSeq);

        node = await node.RestartAsync();
        VerifiedEntry third = Samples.Message(TestKeys.BobDesktop, id, 2, EntryParents.Of(second.Hash), "three");
        Assert.Equal(4UL, (await node.PutFromPeerAsync(Samples.Admit(third, node.Trust), TestKeys.BobDesktop)).StoreSeq);

        var seqs = (await node.RowsAsync<EntryRow>()).ToDictionary(r => new ObjectHash(RowKeys.Hash(r.Hash)), r => r.StoreSeq);
        Assert.Equal(3UL, seqs[first.Hash]);
        Assert.Equal(2UL, seqs[second.Hash]);
        Assert.Equal(4UL, seqs[third.Hash]);
        Assert.Equal(4UL, node.Writer.Totals.StoreSeq);
        await node.DisposeAsync();
    }

    // LOCAL-SPEC.md §1.2, §9.3: the store epoch is a random id created with the database and changed only when it is
    // restored, reset or created anew; reopening keeps it, a new database has another.
    [Fact]
    public async Task TheStoreEpochIsCreatedOnceWithTheDatabaseAndStableAcrossReopening()
    {
        using var environment = new InMemoryStorageEnvironment();
        Hash32 created;
        await using (Database database = await Database.OpenAsync(environment, DatabaseOptions.Default, CancellationToken.None))
        await using (MeshDatabase mesh = await MeshDatabase.OpenAsync(database, new DeterministicEntropy(1), CancellationToken.None))
            created = mesh.Epoch;

        await using (Database database = await Database.OpenAsync(environment, DatabaseOptions.Default, CancellationToken.None))
        await using (MeshDatabase mesh = await MeshDatabase.OpenAsync(database, new DeterministicEntropy(2), CancellationToken.None))
            Assert.Equal(created, mesh.Epoch);

        using var other = new InMemoryStorageEnvironment();
        await using Database fresh = await Database.OpenAsync(other, DatabaseOptions.Default, CancellationToken.None);
        await using MeshDatabase anew = await MeshDatabase.OpenAsync(fresh, new DeterministicEntropy(2), CancellationToken.None);
        Assert.False(created.IsZero);
        Assert.NotEqual(created, anew.Epoch);
    }

    // A database made by another schema version is refused before anything is changed, saying who must act and how.
    [Fact]
    public async Task ADatabaseMadeByAnotherSchemaVersionIsRefusedWithTheStepsOut()
    {
        using var environment = new InMemoryStorageEnvironment();
        await using Database database = await Database.OpenAsync(environment, DatabaseOptions.Default, CancellationToken.None);
        await (await MeshDatabase.OpenAsync(database, new DeterministicEntropy(1), CancellationToken.None)).DisposeAsync();
        await using (LdbContext context = await LdbContext.OpenAsync(database, CancellationToken.None))
        {
            MetaRow version = (await context.Set<MetaRow>().FindAsync(MetaKeys.SchemaVersion, CancellationToken.None))!;
            version.Number = MeshDatabase.SchemaVersion + 1;
            await context.SaveChangesAsync(CancellationToken.None);
        }

        MeshStoreException refused = await Assert.ThrowsAsync<MeshStoreException>(() => MeshDatabase.OpenAsync(database, new DeterministicEntropy(1), CancellationToken.None));

        Assert.Equal(MeshFault.Environment, refused.Fault);
        Assert.False(refused.IsTransient);
        Assert.Equal(2, refused.RecoverySteps.Count);
    }

    // STRUCTURAL-DEBT.md 56, release notes of 0.1.0: a store written by a build before 0.1.0 (schema version 1, Entries in
    // the layout before SPEC.md draft 5, no Parents table) is refused at start; there is no migration, so the steps say to
    // delete the pre-release store and create the identity again.
    [Fact]
    public async Task AStoreOfAPreReleaseBuildIsRefusedAtStart_AndTheStepsSayToDeleteIt()
    {
        using var environment = new InMemoryStorageEnvironment();
        await using Database database = await Database.OpenAsync(environment, DatabaseOptions.Default, CancellationToken.None);
        await (await MeshDatabase.OpenAsync(database, new DeterministicEntropy(1), CancellationToken.None)).DisposeAsync();
        await using (LdbContext context = await LdbContext.OpenAsync(database, CancellationToken.None))
        {
            MetaRow version = (await context.Set<MetaRow>().FindAsync(MetaKeys.SchemaVersion, CancellationToken.None))!;
            version.Number = 1;
            await context.SaveChangesAsync(CancellationToken.None);
        }

        MeshStoreException refused = await Assert.ThrowsAsync<MeshStoreException>(() => MeshDatabase.OpenAsync(database, new DeterministicEntropy(1), CancellationToken.None));

        Assert.Equal(2ul, MeshDatabase.SchemaVersion);
        Assert.Equal(MeshFault.Environment, refused.Fault);
        Assert.False(refused.IsTransient);
        Assert.Contains("pre-release", refused.FriendlyMessage, System.StringComparison.Ordinal);
        Assert.Contains("no migration", refused.FriendlyMessage, System.StringComparison.Ordinal);
        Assert.StartsWith("Stop the Host and delete the pre-release store", refused.RecoverySteps[0], System.StringComparison.Ordinal);
        Assert.Contains("identity create", refused.RecoverySteps[1], System.StringComparison.Ordinal);
    }
}
