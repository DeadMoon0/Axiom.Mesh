using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Benchmarks.Scenarios;

/// <summary>
/// <c>PutObject</c> of 1 MiB Chunks over the real pipe from a service connection (STRUCTURAL-DEBT.md 57): 256 new Chunks
/// with one request outstanding at a time, then with 8, 16 and 32. Each Chunk is stamped and sealed in its own buffer
/// as a file reader would, so every mode pays the same client work.
/// </summary>
internal static class PutObjectScenario
{
    private const int Chunks = 256;

    public static async Task RunAsync(TextWriter output)
    {
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync([TwoNodes.Alice], [], [], ScenarioRunner.None).ConfigureAwait(false);
        await using LocalClient service = await mesh.ConnectCliServiceAsync(TwoNodes.Alice, ScenarioRunner.None).ConfigureAwait(false);
        long seed = 0;
        foreach (int window in new[] { 1, 8, 16, 32 })
        {
            var watch = Stopwatch.StartNew();
            await PutAsync(service, window, Chunks, () => ++seed).ConfigureAwait(false);
            TimeSpan took = watch.Elapsed;
            output.WriteLine($"Result: {window,2} in flight: {Chunks} MiB in {took.TotalSeconds:0.00} s = {ScenarioRunner.Rate((long)Chunks << 20, took)}.");
        }
    }

    // Puts new Chunks with up to window requests outstanding, each with a buffer of its own until answered.
    private static async Task PutAsync(LocalClient service, int window, int count, Func<long> nextSeed)
    {
        var inFlight = new Queue<(byte[] Buffer, Task<LocalResult<StoredObject>> Put)>();
        var free = new Stack<byte[]>();
        for (int i = 0; i < count; i++)
        {
            byte[] buffer;
            if (inFlight.Count == window)
            {
                (buffer, Task<LocalResult<StoredObject>> put) = inFlight.Dequeue();
                Check(await put.ConfigureAwait(false));
            }
            else
            {
                buffer = free.TryPop(out byte[]? spare) ? spare : new byte[Chunker.MaxChunkObjectLength];
            }

            Samples.Stamp(buffer.AsSpan(Chunker.HeaderLength), nextSeed());
            Chunker.Seal(buffer);
            inFlight.Enqueue((buffer, service.SendAsync(new PutObject { Object = buffer }, ScenarioRunner.None).AsTask()));
        }

        while (inFlight.TryDequeue(out (byte[] Buffer, Task<LocalResult<StoredObject>> Put) last))
            Check(await last.Put.ConfigureAwait(false));
    }

    private static void Check(LocalResult<StoredObject> put)
    {
        if (!put.IsOk || !put.Value.Stored)
            throw new InvalidOperationException($"A put was not stored: {put}.");
    }
}
