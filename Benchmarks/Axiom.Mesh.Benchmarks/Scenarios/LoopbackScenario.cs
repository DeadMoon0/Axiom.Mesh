using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Benchmarks.Scenarios;

/// <summary>
/// 1 GiB of Chunks over loopback with real TLS (plan §5: at least 400 MiB/s): Alice stores 16 files of 64 MiB in one
/// Version of an Archive; the clock runs from Bob's follow until the mesh settled with every byte at Bob.
/// </summary>
internal static class LoopbackScenario
{
    private const int Files = 16;
    private const int ChunksPerFile = 64;

    public static async Task RunAsync(TextWriter output)
    {
        await using RealMeshFixture mesh = await TwoNodes.StartAsync().ConfigureAwait(false);
        PackageId photos = await TwoNodes.CreateAsync(mesh, "Photos", PackageKind.Archive).ConfigureAwait(false);
        MeshNode alice = mesh.Node(TwoNodes.Alice);
        var entries = new List<TreeEntry>();
        byte[] data = new byte[Limits.ChunkLength];
        for (int file = 0; file < Files; file++)
        {
            var hashes = new List<ObjectHash>(ChunksPerFile);
            for (int part = 0; part < ChunksPerFile; part++)
            {
                Samples.Stamp(data, (file << 16) | part);
                Chunk chunk = ObjectBuilders.Chunk(data);
                await alice.PutAsync(chunk, ScenarioRunner.None).ConfigureAwait(false);
                hashes.Add(chunk.Hash);
            }

            ChunkList list = ObjectBuilders.ChunkList((ulong)ChunksPerFile * Limits.ChunkLength, hashes);
            await alice.PutAsync(list, ScenarioRunner.None).ConfigureAwait(false);
            entries.Add(new TreeEntry($"file-{file:00}.bin", TreeEntryMode.File, list.Hash, list.Size));
        }

        if (!ObjectBuilders.TryTree(entries, out Tree? root, out string? problem))
            throw new InvalidOperationException(problem);
        await alice.PutAsync(root, ScenarioRunner.None).ConfigureAwait(false);
        await alice.WriteEntryAsync(photos, new VersionPayload { Tree = root.Hash, Label = "main", Prerelease = string.Empty, Notes = string.Empty }.ToBody(), ScenarioRunner.None).ConfigureAwait(false);
        await mesh.SettleAsync(ScenarioRunner.None).ConfigureAwait(false);

        using AllocationListener? sampled = AllocationListener.Requested ? new AllocationListener() : null;
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        await TwoNodes.FollowAsync(mesh, photos).ConfigureAwait(false);
        await ScenarioRunner.SettleLongAsync(() => mesh.SettleAsync(ScenarioRunner.None), TimeSpan.FromMinutes(5)).ConfigureAwait(false);
        TimeSpan took = watch.Elapsed;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        await using LocalClient bob = await mesh.ConnectUserAsync(TwoNodes.Bob, "benchmark", ScenarioRunner.None).ConfigureAwait(false);
        Store store = (await bob.SendAsync(new GetStore(), ScenarioRunner.None).ConfigureAwait(false)).Value;
        long moved = (long)Files * ChunksPerFile * Limits.ChunkLength;
        output.WriteLine($"Result: {moved >> 20} MiB in {took.TotalSeconds:0.00} s = {ScenarioRunner.Rate(moved, took)}; Bob holds {store.Bytes >> 20} MiB.");
        output.WriteLine($"Allocated in the process (both nodes, both TLS ends): {allocated / (double)(1 << 20):0} MiB, {allocated / (double)(Files * ChunksPerFile) / (1 << 20):0.00} MiB per 1 MiB Chunk.");
        if (sampled is not null)
            output.WriteLine($"Sampled by type: {sampled.Top(8)}.");
    }
}
