using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §8: an operation belongs to its connection; it ends Cancelled (status Cancelled) by
/// <c>CancelOperation</c>, by <c>Drain</c> and by the connection's end; at most 16 run at once (<c>Busy</c>), and the
/// records of the last 48 ended ones are kept, the oldest dropped beyond.
/// </summary>
public sealed class OperationTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task CancelOperationDrainAndAClosedConnectionEndAnOperationCancelled_A17thRunningIsBusy_The49thEndedDropsTheOldest()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];
        var held = new HeldOperations();
        held.Install(node.Node.Local);

        // CancelOperation: the record answered is Cancelled, and the operation's last event says so after its first.
        LocalClient client = await node.ConnectUserAsync("ops", None);
        Subscription<OperationChanged> events = (await client.SubscribeAsync<OperationChanged>(Filter.Any, None)).Value;
        uint first = (await client.SendAsync(new Fetch { PackageId = default, Hash = default }, None)).Value;
        Operation cancelled = (await client.SendAsync(new CancelOperation { OperationId = first }, None)).Value;
        Assert.Equal(OperationState.Cancelled, cancelled.State);
        Assert.Equal(Status.Cancelled, cancelled.Status);
        List<OperationChanged> seen = await FirstAsync(events, 2);
        Assert.Equal([OperationState.Running, OperationState.Cancelled], seen.Select(e => e.Operation.State));
        Assert.Equal(OperationState.Cancelled, (await client.SendAsync(new CancelOperation { OperationId = first }, None)).Value.State);

        // 16 running, the 17th Busy.
        var running = new List<uint>();
        for (int i = 0; i < Limits.MaxRunningOperations; i++)
            running.Add((await client.SendAsync(new Fetch { PackageId = default, Hash = default }, None)).Value);
        Assert.Equal(Status.Busy, (await client.SendAsync(new Fetch { PackageId = default, Hash = default }, None)).Status);

        // The connection ends: every running operation is cancelled with it.
        await client.ByeAsync(None);
        await Waits.UntilAsync(() => held.Cancelled == 1 + Limits.MaxRunningOperations, "every running operation's cancellation");

        // 49 ended on one connection: 48 kept, the oldest dropped.
        var quick = new HeldOperations { Immediate = true };
        quick.Install(node.Node.Local);
        LocalClient many = await node.ConnectUserAsync("many", None);
        var ids = new List<uint>();
        for (int i = 0; i < Limits.MaxEndedOperations + 1; i++)
        {
            ids.Add((await many.SendAsync(new Fetch { PackageId = default, Hash = default }, None)).Value);
            await UntilEndedAsync(many, ids[^1]);
        }

        IReadOnlyList<Operation> kept = (await many.SendAsync(new ListOperations(), None)).Value;
        Assert.Equal(ids.Skip(1), kept.Select(o => o.OperationId));
        Assert.All(kept, o => Assert.Equal(OperationState.Done, o.State));
        Assert.Equal(Status.NotFound, (await many.SendAsync(new GetOperation { OperationId = ids[0] }, None)).Status);

        // Drain: a running operation ends Cancelled, and its final event follows the Drain.
        held.Install(node.Node.Local);
        LocalClient draining = await node.ConnectUserAsync("draining", None);
        Subscription<OperationChanged> drained = (await draining.SubscribeAsync<OperationChanged>(Filter.Any, None)).Value;
        uint last = (await draining.SendAsync(new Fetch { PackageId = default, Hash = default }, None)).Value;
        Task stopping = node.Node.Local.DrainAsync(None);
        List<OperationChanged> final = await FirstAsync(drained, 2);
        Assert.Equal(last, final[^1].Operation.OperationId);
        Assert.Equal(OperationState.Cancelled, final[^1].Operation.State);
        Assert.Equal(Status.Cancelled, final[^1].Operation.Status);
        await Waits.UntilAsync(() => draining.Draining.IsCancellationRequested, "the Drain");
        mesh.Clock.Advance(TimeSpan.FromMilliseconds(Limits.DrainGraceDefaultMilliseconds));
        await stopping.WaitAsync(TestMesh.Deadline);
    }

    private static async Task<List<OperationChanged>> FirstAsync(Subscription<OperationChanged> events, int count)
    {
        var seen = new List<OperationChanged>();
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        await foreach (OperationChanged changed in events.WithCancellation(deadline.Token))
        {
            seen.Add(changed);
            if (seen.Count == count)
                break;
        }

        return seen;
    }

    private static async Task UntilEndedAsync(LocalClient client, uint operation)
    {
        var watch = Stopwatch.StartNew();
        while ((await client.SendAsync(new GetOperation { OperationId = operation }, None)).Value.State == OperationState.Running)
        {
            if (watch.Elapsed > TestMesh.Deadline)
                throw new TimeoutException($"Operation {operation} did not end.");
            await Task.Yield();
        }
    }
}
