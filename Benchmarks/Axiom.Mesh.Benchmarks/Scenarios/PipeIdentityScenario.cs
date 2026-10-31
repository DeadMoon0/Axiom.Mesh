using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Benchmarks.Scenarios;

/// <summary>
/// <c>GetIdentity</c> over the real pipe (plan §5: p50 at most 200 µs, p99 under 1 ms, at most 2 allocations in the Host):
/// one user connection, round trips one after the other, timed each. Allocations are counted for the whole process,
/// client, pipe and Host together; the Host's own share is bounded by the regression tests.
/// </summary>
internal static class PipeIdentityScenario
{
    private const int Warmup = 2_000;
    private const int Samples = 20_000;

    public static async Task RunAsync(TextWriter output)
    {
        await using RealMeshFixture mesh = await RealMeshFixture.StartAsync([TwoNodes.Alice], [], [], ScenarioRunner.None).ConfigureAwait(false);
        await using LocalClient client = await mesh.ConnectUserAsync(TwoNodes.Alice, "benchmark", ScenarioRunner.None).ConfigureAwait(false);
        var request = new GetIdentity();
        for (int i = 0; i < Warmup; i++)
            await client.SendAsync(request, ScenarioRunner.None).ConfigureAwait(false);

        double[] micros = new double[Samples];
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        for (int i = 0; i < Samples; i++)
        {
            long start = Stopwatch.GetTimestamp();
            LocalResult<Identity> answer = await client.SendAsync(request, ScenarioRunner.None).ConfigureAwait(false);
            micros[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            if (!answer.IsOk)
                throw new InvalidOperationException(answer.ToString());
        }

        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        Array.Sort(micros);
        output.WriteLine($"Result: {Samples} round trips: p50 {ScenarioRunner.Percentile(micros, 50):0} µs, p99 {ScenarioRunner.Percentile(micros, 99):0} µs, max {micros[^1]:0} µs.");
        output.WriteLine($"Allocated in the process (client, pipe and Host): {allocated / Samples} bytes per round trip.");
    }
}
