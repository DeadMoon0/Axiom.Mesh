using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Benchmarks.Scenarios;

/// <summary>
/// Runs one scenario on real nodes and prints what it measured, with the machine it ran on: the plan §5 rows that move a
/// gigabyte or ten thousand Entries, or that need percentiles of a pipe round trip, run once each instead of in
/// BenchmarkDotNet's repeated iterations.
/// </summary>
internal static class ScenarioRunner
{
    private static readonly Dictionary<string, (string What, Func<TextWriter, Task> Run)> All = new(StringComparer.Ordinal)
    {
        ["loopback"] = ("1 GiB of Chunks between two nodes over loopback with real TLS", LoopbackScenario.RunAsync),
        ["walk"] = ("a history of 10 000 Entries walked between two nodes over loopback with real TLS", HistoryWalkScenario.RunAsync),
        ["identity"] = ("GetIdentity over the real pipe: percentiles and allocations", PipeIdentityScenario.RunAsync),
        ["putobject"] = ("PutObject of 1 MiB Chunks over the real pipe, one at a time and pipelined", PutObjectScenario.RunAsync),
        ["putfile"] = ("StoreFiles.PutFileAsync of a 256 MiB file over the real pipe", PutFileScenario.RunAsync),
        ["forward"] = ("where forwarding a 1 MiB Object allocates: the frame layer in memory, then over loopback TLS", ForwardScenario.RunAsync),
        ["chat-send"] = ("a 1 MiB chat attachment from chat send on one node to chat get on another, loopback TLS and the real pipe", ChatScenario.SendAsync),
        ["chat-catchup"] = ("10 000 chat messages caught up by a restarted chat service over the real pipe", ChatScenario.CatchUpAsync),
        ["sync"] = ("the sync service on real nodes: 10 000 files and 64 MiB end to end, an unchanged 100 000-file scan, a change to a Version", SyncScenario.RunAsync),
    };

    /// <summary>Runs the named scenario, or lists them.</summary>
    /// <param name="args">The scenario's name.</param>
    /// <param name="output">Where results go.</param>
    /// <returns>0 when it ran, 1 for an unknown name.</returns>
    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, TextWriter output)
    {
        if (args.Length == 0 || !All.TryGetValue(args.Span[0], out (string What, Func<TextWriter, Task> Run) scenario))
        {
            output.WriteLine("Scenarios (dotnet run -c Release -- scenario <name>):");
            foreach ((string name, (string what, _)) in All)
                output.WriteLine($"  {name,-10} {what}");
            return 1;
        }

        output.WriteLine($"{args.Span[0]}: {scenario.What}");
        output.WriteLine($"Machine: {Machine()}");
        output.WriteLine($"Date: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC");
        await scenario.Run(output).ConfigureAwait(false);
        return 0;
    }

    /// <summary>The CPU, the logical cores, the operating system and the runtime.</summary>
    /// <returns>One line.</returns>
    public static string Machine() =>
        $"{Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString()}, {Environment.ProcessorCount} logical cores; {RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}";

    /// <summary>Settles a real mesh with a bound of its own: the fixture's settle gives up after 15 s, so it is asked again.</summary>
    /// <param name="settle">One settle.</param>
    /// <param name="bound">How long to keep asking.</param>
    /// <returns>A task that completes once settled.</returns>
    public static async Task SettleLongAsync(Func<Task> settle, TimeSpan bound)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                await settle().ConfigureAwait(false);
                return;
            }
            catch (Axiom.Mesh.Testing.TestMeshException) when (watch.Elapsed < bound)
            {
                // Still moving: settle again.
            }
        }
    }

    /// <summary>The value at a percentile of sorted samples (nearest rank).</summary>
    /// <param name="sorted">Ascending samples.</param>
    /// <param name="percentile">0 to 100.</param>
    /// <returns>The sample.</returns>
    public static double Percentile(IReadOnlyList<double> sorted, double percentile) =>
        sorted[Math.Clamp((int)Math.Ceiling(percentile / 100 * sorted.Count) - 1, 0, sorted.Count - 1)];

    /// <summary>A cancellation-free token for the scenarios.</summary>
    public static CancellationToken None => CancellationToken.None;

    /// <summary>Formats mebibytes per second.</summary>
    /// <param name="bytes">Bytes moved.</param>
    /// <param name="took">In how long.</param>
    /// <returns>The rate.</returns>
    public static string Rate(long bytes, TimeSpan took) => $"{bytes / (double)(1 << 20) / took.TotalSeconds:0} MiB/s";
}
