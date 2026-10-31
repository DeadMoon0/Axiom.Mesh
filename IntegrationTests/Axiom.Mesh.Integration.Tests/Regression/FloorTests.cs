using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Testing;
using Xunit.Abstractions;

namespace Axiom.Mesh.Integration.Tests.Regression;

/// <summary>
/// The release floors of the fundamentals plan §5, on real nodes over loopback with real TLS and real disks: far below the
/// benchmark targets, they catch an order-of-magnitude regression, not a percentage. They run only with
/// <c>AXIOM_MESH_FLOORS=1</c> in a Release build (<see cref="FloorFactAttribute"/>) and print what they measured.
/// </summary>
public sealed class FloorTests(ITestOutputHelper output)
{
    private const int Mebibyte = 1 << 20;
    private static readonly CancellationToken None = CancellationToken.None;

    // Damage: a transfer path that copied every object several times, hashed it twice, stalled the TLS writer or committed
    // every Chunk in a transaction of its own would push 1 GiB over loopback past a minute.
    [FloorFact]
    [Trait("Category", "Floor")]
    public async Task OneGibibyteOfChunksCrossesLoopbackWithRealTlsWithinSixtySeconds()
    {
        const int Files = 16;
        const int ChunksPerFile = 64;
        await using RealMeshFixture mesh = await PairedAsync();
        PackageId photos = await CreateAsync(mesh, "Photos", PackageKind.Archive);
        MeshNode alice = mesh.Node("alice-laptop");
        var entries = new List<TreeEntry>();
        byte[] chunk = new byte[Mebibyte];
        for (int file = 0; file < Files; file++)
        {
            var hashes = new List<ObjectHash>(ChunksPerFile);
            for (int part = 0; part < ChunksPerFile; part++)
            {
                Fill(chunk, file, part);
                Chunk stored = ObjectBuilders.Chunk(chunk);
                Assert.True(await alice.PutAsync(stored, None));
                hashes.Add(stored.Hash);
            }

            ChunkList list = ObjectBuilders.ChunkList((ulong)ChunksPerFile * Mebibyte, hashes);
            Assert.True(await alice.PutAsync(list, None));
            entries.Add(new TreeEntry($"file-{file:00}.bin", TreeEntryMode.File, list.Hash, list.Size));
        }

        Assert.True(ObjectBuilders.TryTree(entries, out Tree? root, out string? problem), problem);
        Assert.True(await alice.PutAsync(root, None));
        await alice.WriteEntryAsync(photos, new VersionPayload { Tree = root.Hash, Label = "main", Prerelease = string.Empty, Notes = string.Empty }, None);
        await mesh.SettleAsync(None);

        var watch = Stopwatch.StartNew();
        await FollowAsync(mesh, photos);
        await SettleLongAsync(mesh, TimeSpan.FromSeconds(60));
        TimeSpan took = watch.Elapsed;

        Store store = await StoreAsync(mesh, "bob-desktop");
        output.WriteLine($"1 GiB of Chunks over loopback with TLS: {took.TotalSeconds:0.0} s, {Files * ChunksPerFile / took.TotalSeconds:0} MiB/s; Bob holds {store.Bytes / (double)Mebibyte:0} MiB.");
        Assert.True(store.Bytes >= (ulong)Files * ChunksPerFile * Mebibyte, $"Bob holds {store.Bytes} bytes.");
        Assert.True(took <= TimeSpan.FromSeconds(60), $"1 GiB took {took.TotalSeconds:0.0} s.");
    }

    // Damage: a history walk that asked for one parent per round trip with a commit each, or re-read the whole history for
    // every Entry it stored, would take far more than two minutes for 10 000 Entries.
    [FloorFact]
    [Trait("Category", "Floor")]
    public async Task AHistoryOfTenThousandEntriesIsWalkedWithinTwoMinutes()
    {
        const int Entries = 10_000;
        await using RealMeshFixture mesh = await PairedAsync();
        PackageId channel = await CreateAsync(mesh, "Family", PackageKind.Channel);
        MeshNode alice = mesh.Node("alice-laptop");
        for (int i = 0; i < Entries; i++)
            await alice.WriteEntryAsync(channel, new MessagePayload { Text = $"message {i}", Attachments = [], ReplyTo = default }, None);
        await mesh.SettleAsync(None);

        var watch = Stopwatch.StartNew();
        await FollowAsync(mesh, channel);
        await SettleLongAsync(mesh, TimeSpan.FromSeconds(120));
        TimeSpan took = watch.Elapsed;

        await using LocalClient bob = await mesh.ConnectUserAsync("bob-desktop", "floor", None);
        Package held = (await bob.SendAsync(new GetPackage { PackageId = channel }, None)).Value;
        output.WriteLine($"A history of {Entries} Entries walked over loopback with TLS: {took.TotalSeconds:0.0} s, {Entries / took.TotalSeconds:0} Entries/s.");
        Assert.Equal((ulong)Entries, held.Entries);
        Assert.True(took <= TimeSpan.FromSeconds(120), $"The walk took {took.TotalSeconds:0.0} s.");
    }

    // Distinct bytes per Chunk, cheaply: the file and part numbers stamped over a fixed pattern.
    private static void Fill(byte[] chunk, int file, int part)
    {
        for (int i = 0; i < chunk.Length; i += 8)
            BitConverter.TryWriteBytes(chunk.AsSpan(i), ((long)file << 48) | ((long)part << 32) | (uint)i);
    }

    private static async Task<RealMeshFixture> PairedAsync()
    {
        RealMeshFixture mesh = await RealMeshFixture.StartAsync(["alice-laptop", "bob-desktop"], [], [], None);
        await mesh.PairAsync("alice-laptop", "bob-desktop", None);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        return mesh;
    }

    private static async Task<PackageId> CreateAsync(RealMeshFixture mesh, string name, PackageKind kind)
    {
        await mesh.HoldUserKeyAsync("alice-laptop", None);
        await using LocalClient alice = await mesh.ConnectUserAsync("alice-laptop", "floor", None);
        PackageId package = (await alice.SendAsync(new CreatePackage { Kind = kind, Name = name }, None)).Value.PackageId;
        Assert.True((await alice.SendAsync(new SetGrant { PackageId = package, GranteeId = TestIdentity.Of("bob-desktop").UserId, Rights = GrantRights.Read }, None)).IsOk);
        return package;
    }

    private static async Task FollowAsync(RealMeshFixture mesh, PackageId package)
    {
        await using LocalClient bob = await mesh.ConnectUserAsync("bob-desktop", "floor", None);
        Assert.True((await bob.SendAsync(new FollowPackage { PackageId = package }, None)).IsOk);
    }

    private static async Task<Store> StoreAsync(RealMeshFixture mesh, string node)
    {
        await using LocalClient client = await mesh.ConnectUserAsync(node, "floor", None);
        return (await client.SendAsync(new GetStore(), None)).Value;
    }

    // The fixture's settle gives up after 15 s; a floor allows its own bound, settling again until it passes.
    private static async Task SettleLongAsync(RealMeshFixture mesh, TimeSpan bound)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                await mesh.SettleAsync(None);
                return;
            }
            catch (TestMeshException) when (watch.Elapsed < bound)
            {
                // Still moving: settle again.
            }
        }
    }
}
