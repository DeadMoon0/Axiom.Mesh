using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Integration.Tests.Support;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Benchmarks.Scenarios;

/// <summary>
/// A history walk (plan §5: 10 000 Entries within 10 s): Alice writes a linear Channel of 10 000 Messages; the clock runs
/// from Bob's follow until the mesh settled with every Entry at Bob. A linear history names each parent only in its
/// child, so the walk is one round trip per Entry by nature; what the walk may not add is a commit per round trip.
/// </summary>
internal static class HistoryWalkScenario
{
    private const int Entries = 10_000;

    public static async Task RunAsync(TextWriter output)
    {
        await using RealMeshFixture mesh = await TwoNodes.StartAsync().ConfigureAwait(false);
        PackageId channel = await TwoNodes.CreateAsync(mesh, "Family", PackageKind.Channel).ConfigureAwait(false);
        MeshNode alice = mesh.Node(TwoNodes.Alice);
        for (int i = 0; i < Entries; i++)
            await alice.WriteEntryAsync(channel, new MessagePayload { Text = $"message {i}", Attachments = [], ReplyTo = default }.ToBody(), ScenarioRunner.None).ConfigureAwait(false);
        await mesh.SettleAsync(ScenarioRunner.None).ConfigureAwait(false);

        var watch = Stopwatch.StartNew();
        await TwoNodes.FollowAsync(mesh, channel).ConfigureAwait(false);
        await ScenarioRunner.SettleLongAsync(() => mesh.SettleAsync(ScenarioRunner.None), TimeSpan.FromMinutes(5)).ConfigureAwait(false);
        TimeSpan took = watch.Elapsed;

        await using LocalClient bob = await mesh.ConnectUserAsync(TwoNodes.Bob, "benchmark", ScenarioRunner.None).ConfigureAwait(false);
        Package held = (await bob.SendAsync(new GetPackage { PackageId = channel }, ScenarioRunner.None).ConfigureAwait(false)).Value;
        output.WriteLine($"Result: {held.Entries} of {Entries} Entries in {took.TotalSeconds:0.00} s = {Entries / took.TotalSeconds:0} Entries/s, {took.TotalMilliseconds / Entries:0.00} ms per Entry.");
    }
}
