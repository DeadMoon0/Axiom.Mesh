using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Protocol;
using Axiom.Mesh.Tests.Shared;
using Axiom.ProtoStream.Codecs;
using WireState = Axiom.Mesh.Protocol.PackageState;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>What replication does today, pinned: listing pages, the send path's allocations, the counters.</summary>
[Collection(nameof(WholeProcessMeasurement))]
public sealed class ReplicationRegressionTests
{
    private const int MebibyteObject = Limits.ChunkLength - 2;
    private static readonly CancellationToken None = CancellationToken.None;

    // Damage: a listing cut wrong (a page over 64, a page number skipped, last set twice or never, a Package listed twice
    // or out of order, a page past the frame bound) would break every peer's ListingRule.
    [Fact]
    public void AListingOf1000PackagesIsSixteenPagesInOrderEachWithinAFrame()
    {
        var keys = new Dictionary<DeviceId, PublicKey>();
        var builder = new TrustBuilder(TrustSnapshot.Empty, keys);
        builder.SetSelf(new SelfIdentity(TestKeys.Alice.PublicKey, TestKeys.AliceLaptop.PublicKey.AsDeviceId()));
        for (int i = 0; i < 1000; i++)
        {
            VerifiedManifest manifest = new ManifestDraft { Nonce = new PackageNonce(Hash32.Of(BitConverter.GetBytes(i))), Kind = PackageKind.Channel, Seq = 1, Name = "p" + i, Created = Samples.Created }.Sign(TestKeys.Alice);
            builder.SetState(manifest.Content.Package, PackageState.Followed);
            builder.SetManifest(manifest);
        }

        TrustSnapshot trust = builder.Build();
        UserId bob = TestKeys.Bob.PublicKey.AsUserId();
        UserId alice = TestKeys.Alice.PublicKey.AsUserId();

        Packages[] pages = [.. Listings.Pages(alice, Listings.Listable(alice, trust), trust, HeadsView.Empty).Cast<Packages>()];

        Assert.Empty(Listings.Listable(bob, trust));
        Assert.Equal(16, pages.Length);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (ushort)i), pages.Select(p => p.Page));
        Assert.Equal(Enumerable.Repeat(false, 15).Append(true), pages.Select(p => p.Last));
        Assert.Equal(Enumerable.Repeat(64, 15).Append(40), pages.Select(p => p.States.Count));
        PackageId[] listed = [.. pages.SelectMany(p => p.States).Select(s => s.Package)];
        Assert.Equal(listed.Order(), listed);
        Assert.Equal(1000, listed.Distinct().Count());
        using var codec = new CodecRunner<MeshMessage, MeshMessage>(Amp.Definition);
        foreach (Packages page in pages)
        {
            var frame = new ArrayBufferWriter<byte>();
            codec.Encode(page, frame);
            Assert.InRange(frame.WrittenCount, 1, Limits.MaxFrameLength);
        }
    }

    // Damage: a send path that copied each object into a fresh array (reading the file into a new buffer, or building the
    // frame in its own), or an intake that did, would allocate another mebibyte per object and pass the budget.
    [ReleaseFact]
    public async Task ForwardingAMebibyteObjectAllocatesNoFurtherWholeCopy()
    {
        const int Files = 16;
        await using TestMesh mesh = await Meshes.AliceAndBobAsync();
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        PackageId photos = await alice.CreatePackageAsync("Photos", PackageKind.Archive, Retention.Default, None);
        await alice.GrantAsync(photos, "bob", GrantRights.Read, None);
        // The pools fill on a first round of the same size: every buffer a round can hold at once is rented once.
        TreeFixture warmup = ObjectFixtures.Folder([.. Enumerable.Range(1, Files).Select(i => ($"w{i:00}.bin", ObjectFixtures.Content(MebibyteObject, (byte)(100 + i))))]);
        TreeFixture folder = ObjectFixtures.Folder([.. Enumerable.Range(1, Files).Select(i => ($"f{i:00}.bin", ObjectFixtures.Content(MebibyteObject, (byte)i)))]);
        ObjectHash first = await alice.PublishAsync(photos, warmup, None);
        await bob.FollowAsync(photos, None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        foreach (Chunk chunk in warmup.Chunks)
            await bob.WaitForObjectAsync(chunk.Hash, None);
        await mesh.SettleAsync(None);

        long before = GC.GetTotalAllocatedBytes(precise: true);
        await alice.PublishAsync(photos, folder, None);
        foreach (Chunk chunk in folder.Chunks)
            await bob.WaitForObjectAsync(chunk.Hash, None);
        await mesh.SettleAsync(None);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        // Measured on .NET 8 and 10: 4.2 to 4.7 MiB per object, of which 2 MiB are the copies Alice's and Bob's in-memory
        // object files keep, 1 MiB is intake's pooled copy as the pool grows, and the rest TLS, pipes and frames
        // (STRUCTURAL-DEBT.md 27). One more whole copy anywhere on the way passes 5 MiB.
        Assert.True(first != default);
        Assert.InRange(allocated, Files * 2L * Limits.ChunkLength, Files * 5L * Limits.ChunkLength);
    }

    // Damage: a counter not counted (an object received or sent, an Entry stored, a violation) would leave the Host's
    // telemetry and the Session record blind.
    [Fact]
    public async Task TheCountersCountObjectsInAndOutEntriesStoredAndViolations()
    {
        await using TestMesh mesh = await Meshes.AliceAndBobAsync();
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        var measured = new Dictionary<string, long>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (ReferenceEquals(instrument.Meter, bob.Node.Replicating.Telemetry.Meter))
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            lock (measured)
                measured[instrument.Name] = measured.GetValueOrDefault(instrument.Name) + value;
        });
        listener.Start();
        PackageId channel = await alice.CreatePackageAsync("Family", PackageKind.Channel, Retention.Default, None);
        await alice.GrantAsync(channel, "bob", GrantRights.Read, None);
        IReadOnlyList<ObjectHash> written = await ObjectFixtures.WriteMessagesAsync(alice, channel, 3, None);

        await bob.FollowAsync(channel, None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        await bob.WaitForEntryAsync(written[0], None);
        await mesh.SettleAsync(None);

        ReplicationStatus bobs = bob.Node.Replication;
        ReplicationStatus alices = alice.Node.Replication;
        Assert.Equal(3, bobs.EntriesStored);
        // The Manifest, the Grant, three Entries; an object a session the duplicate rule closed was answering may come twice.
        Assert.InRange(bobs.ObjectsIn, 5, 10);
        Assert.InRange(alices.ObjectsOut, 5, 10);
        Assert.Equal(0, bobs.Violations);
        lock (measured)
        {
            Assert.Equal(bobs.ObjectsIn, measured["axiom.mesh.objects.in"]);
            Assert.Equal(3, measured["axiom.mesh.entries.stored"]);
            Assert.False(measured.ContainsKey("axiom.mesh.violations"));
        }

        SessionTraffic session = bobs.Sessions.Single();
        Assert.Equal(alice.Device, session.Device);
        Assert.InRange(session.ObjectsIn, 0, 5); // a session the duplicate rule closed may have carried some
        Assert.True(session.BytesIn > 0 && session.BytesOut > 0);
    }
}
