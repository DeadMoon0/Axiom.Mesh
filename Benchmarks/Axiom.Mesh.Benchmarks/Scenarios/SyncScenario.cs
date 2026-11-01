using System;
using System.IO;
using System.Threading.Tasks;
using Axiom.Mesh.Integration.Tests.Support;

namespace Axiom.Mesh.Benchmarks.Scenarios;

/// <summary>
/// The sync service on real nodes over loopback TLS and the real pipe (plan §9.4 item 6), measured by the integration suite's
/// <see cref="SyncMeasure"/>: scenario 6 at full size (10 000 files and 64 MiB, floor 60 s), an unchanged folder of 100 000
/// files scanned (floor 2 s), a changed 1 MiB file a Version after the debounce (floor 1 s).
/// </summary>
internal static class SyncScenario
{
    /// <summary>All three.</summary>
    /// <param name="output">Where results go.</param>
    /// <returns>A task.</returns>
    public static async Task RunAsync(TextWriter output)
    {
        TimeSpan folder = await SyncMeasure.MeasureFolderAsync(10_000, 64).ConfigureAwait(false);
        output.WriteLine($"10 000 files and 64 MiB from Alice's sync add to every byte in Bob's folder: {folder.TotalSeconds:0.00} s (floor 60 s)");
        TimeSpan scan = await SyncMeasure.MeasureUnchangedScanAsync(100_000).ConfigureAwait(false);
        output.WriteLine($"an unchanged folder of 100 000 files scanned: {scan.TotalSeconds:0.00} s (floor 2 s)");
        TimeSpan change = await SyncMeasure.MeasureChangeToVersionAsync().ConfigureAwait(false);
        output.WriteLine($"a changed 1 MiB file a Version after the 2 s debounce: {change.TotalSeconds:0.00} s (floor 1 s)");
    }
}
