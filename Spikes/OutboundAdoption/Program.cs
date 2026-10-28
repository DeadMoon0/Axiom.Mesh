using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Spikes;

using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var log = new SpikeLog(Console.WriteLine);

bool adoption = await AdoptionScenario.RunAsync(41901, log, stop.Token);
bool ldb = await LdbScenario.RunAsync(Path.Combine(Path.GetTempPath(), "axiom-mesh-spike-ldb"), log, stop.Token);

Console.WriteLine();
Console.WriteLine($"outbound adoption: {(adoption ? "PASS" : "FAIL")}");
Console.WriteLine($"LDB on this platform: {(ldb ? "PASS" : "FAIL")}");
return adoption && ldb ? 0 : 1;
