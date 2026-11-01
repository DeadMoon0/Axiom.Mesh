using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Benchmarks.Scenarios;

/// <summary>
/// <c>StoreFiles.PutFileAsync</c> of a 256 MiB file over the real pipe (STRUCTURAL-DEBT.md 57): once with one Chunk in
/// flight, as the helper ran before M12, and once with <see cref="StoreFiles.DefaultChunksInFlight"/>. The two files
/// differ, so the second run stores as much as the first.
/// </summary>
internal static class PutFileScenario
{
    private const int Mebibytes = 256;

    public static async Task RunAsync(TextWriter output)
    {
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync([TwoNodes.Alice], [], [], [], ScenarioRunner.None).ConfigureAwait(false);
        await using LocalClient service = await mesh.ConnectCliServiceAsync(TwoNodes.Alice, ScenarioRunner.None).ConfigureAwait(false);
        string folder = Path.Combine(Path.GetTempPath(), "axiom-mesh-bench", "putfile-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            foreach (int window in new[] { 1, StoreFiles.DefaultChunksInFlight })
            {
                string file = Path.Combine(folder, $"file-{window}.bin");
                Write(file, window);
                var watch = Stopwatch.StartNew();
                LocalResult<StoredFile> stored = await StoreFiles.PutFileAsync(service, file, window, null, ScenarioRunner.None).ConfigureAwait(false);
                TimeSpan took = watch.Elapsed;
                if (!stored.IsOk)
                    throw new InvalidOperationException(stored.ToString());
                output.WriteLine($"Result: {window} in flight: {Mebibytes} MiB in {took.TotalSeconds:0.00} s = {ScenarioRunner.Rate((long)Mebibytes << 20, took)}.");
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // A file of distinct 1 MiB parts, written before the clock starts.
    private static void Write(string path, int seed)
    {
        byte[] part = new byte[Limits.ChunkLength];
        using FileStream file = File.Create(path);
        for (int i = 0; i < Mebibytes; i++)
        {
            Samples.Stamp(part, ((long)seed << 20) | (uint)i);
            file.Write(part);
        }
    }
}
