using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Discovery;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// SPEC.md §4.4 and §12.1: after a failed dial a node waits 10 seconds, doubling with every further failure up to 5
/// minutes; a device that broke the protocol three times within an hour is not dialled for an hour.
/// </summary>
public sealed class BackoffTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // §4.4: Bob's desktop is announced where nothing listens. Each failed dial holds the next one back: 10 s, 20 s, 40 s,
    // 80 s, 160 s, then 5 minutes, and 5 minutes again; never a dial before its time, and the retry comes on its own.
    [Fact]
    public async Task AFailedDialWaits10SecondsThenDoublesToAtMost5Minutes()
    {
        await using TestMesh mesh = await Meshes.AliceKnowingBobAsync();
        TestIdentity bob = TestIdentity.Of("bob-desktop");
        MeshNode alice = mesh["alice-laptop"].Node;
        var silent = new FakeDiscovery(mesh.Network, new Announcement(bob.DeviceId, bob.UserId, new IPEndPoint(IPAddress.Parse("10.0.0.9"), 41810), DiscoverySource.Mdns));

        long seen = alice.Facts.LastSequence;
        silent.Announce();
        foreach (int wait in new[] { 10, 20, 40, 80, 160, 300, 300 })
        {
            NodeFact failed = await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.BackedOff && f.Device == bob.DeviceId, seen, $"the dial before the {wait} s wait failed", None);
            seen = failed.Sequence;
            PeerRecord record = alice.Peers.Single(p => p.Device == bob.DeviceId);
            Assert.Equal(PeerState.BackedOff, record.State);
            Assert.Equal(failed.At + TimeSpan.FromSeconds(wait), record.BackoffUntil);

            mesh.Clock.Advance(TimeSpan.FromSeconds(wait) - TimeSpan.FromMilliseconds(1));
            Assert.Equal(PeerState.BackedOff, alice.Peers.Single(p => p.Device == bob.DeviceId).State);
            mesh.Clock.Advance(TimeSpan.FromMilliseconds(1));
            await mesh.WaitForFactAsync("alice-laptop", f => f.Kind == NodeFactKind.Dialling && f.Device == bob.DeviceId, seen, $"the dial after {wait} s", None);
        }
    }

    // §12.1: violations count within an hour from the first; the third holds dials back for an hour. Pure: the window and
    // the hold take timestamps.
    [Fact]
    public void ThreeViolationsWithinAnHourHoldDialsBackForAnHour_AFourthAfterTheHourStartsAgain()
    {
        DateTimeOffset start = TestIdentity.Epoch;
        ViolationWindow window = default(ViolationWindow).Add(start).Add(start.AddMinutes(30));
        Assert.Equal(TimeSpan.Zero, Backoff.ForViolations(window.Count));

        window = window.Add(start.AddMinutes(59));
        Assert.Equal(TimeSpan.FromHours(1), Backoff.ForViolations(window.Count));

        ViolationWindow later = window.Add(start.AddHours(1));
        Assert.Equal(1, later.Count);
        Assert.Equal(TimeSpan.Zero, Backoff.ForViolations(later.Count));
    }

    [Theory]
    [InlineData(1, 10)] // the first failure: 10 seconds
    [InlineData(2, 20)] // doubling
    [InlineData(5, 160)] // still doubling
    [InlineData(6, 300)] // 320 would pass 5 minutes: capped
    [InlineData(1000, 300)] // no overflow however many failures
    public void TheWaitAfterNFailuresIs10SecondsDoubledNMinus1TimesCappedAt5Minutes(int failures, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), Backoff.Next(failures));
}
