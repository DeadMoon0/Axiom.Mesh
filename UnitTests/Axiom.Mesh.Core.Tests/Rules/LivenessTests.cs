using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// SPEC.md §6.3: a node sends <c>Ping</c> after 60 seconds without sending and answers every <c>Ping</c>; a session that
/// received nothing for 5 minutes is closed. Two nodes with nothing to say keep their session alive on heartbeats alone.
/// </summary>
public sealed class LivenessTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // Six minutes of silence on the fake clock, a minute at a time: every minute both sides ping and are answered, so
    // neither goes 5 minutes without receiving, and the one session stays established throughout.
    [Fact]
    public async Task AnEstablishedSessionSurvivesFiveMinutesOfSilenceWithHeartbeats()
    {
        await using TestMesh mesh = await Meshes.AliceAndBobAsync();
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
        PeerSession alices = mesh["alice-laptop"].Node.Table.EstablishedWith(mesh["bob-desktop"].Device)!;
        PeerSession bobs = mesh["bob-desktop"].Node.Table.EstablishedWith(mesh["alice-laptop"].Device)!;
        long before = mesh["alice-laptop"].Node.Facts.LastSequence;

        for (int minute = 1; minute <= 6; minute++)
        {
            mesh.Clock.Advance(TimeSpan.FromSeconds(60));
            await Waits.UntilAsync(() => alices.Activity.SilentFor() == TimeSpan.Zero && bobs.Activity.SilentFor() == TimeSpan.Zero, $"the heartbeats of minute {minute}");
        }

        Assert.Same(alices, mesh["alice-laptop"].Node.Table.EstablishedWith(mesh["bob-desktop"].Device));
        Assert.Same(bobs, mesh["bob-desktop"].Node.Table.EstablishedWith(mesh["alice-laptop"].Device));
        Assert.DoesNotContain(mesh["alice-laptop"].Node.Facts.Recent(), f => f.Sequence > before && f.Kind is NodeFactKind.SessionEnded or NodeFactKind.Violation);
    }
}
