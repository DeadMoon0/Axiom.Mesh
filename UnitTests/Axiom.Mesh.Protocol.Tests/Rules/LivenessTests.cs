using System;
using System.Threading.Tasks;
using Axiom.Mesh.Protocol.Tests.Support;
using Axiom.ProtoStream;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>Liveness (SPEC.md §6.3), on a fake clock.</summary>
public sealed class LivenessTests
{
    // §6.3: "a node MUST send Ping when it has sent nothing for 60 seconds ... A session on which nothing was received for
    // 5 minutes MUST be closed with Bye(Idle)."
    [Fact]
    public async Task PingAfter60SecondsOfSilence_ByeIdleAfter5MinutesWithoutReceiving()
    {
        await using RawPeer peer = await RawPeer.OpenAsync();
        await peer.EstablishAsync();
        Task<Violation?> reading = peer.ReadUntilViolationAsync();

        peer.Time.Advance(TimeSpan.FromSeconds(Limits.PingAfterSilenceSeconds - 1));
        Assert.False(peer.HasPendingInput(), "A Ping went out before 60 seconds of silence.");
        peer.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.IsType<Ping>(await peer.ReceiveAsync());

        peer.Time.Advance(TimeSpan.FromSeconds(Limits.IdleSeconds - Limits.PingAfterSilenceSeconds - 1));
        Assert.False(reading.IsCompleted, "The session closed before 5 minutes without receiving.");
        peer.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(ViolationCode.Timeout, (await reading)!.Code);
        Assert.Equal(ByeReason.Idle, Assert.IsType<Bye>(await AfterPings(peer)).Reason);
    }

    // §6.3: receiving anything, a Ping included, is what keeps a session alive; the Pong carries the Ping's nonce.
    [Fact]
    public async Task APingFromThePeerIsAnsweredWithItsNonceAndRestartsTheIdleTime()
    {
        await using RawPeer peer = await RawPeer.OpenAsync();
        await peer.EstablishAsync();
        Task<Violation?> reading = peer.ReadUntilViolationAsync();

        peer.Time.Advance(TimeSpan.FromSeconds(200));
        await peer.SendAsync(new Ping(77));
        Assert.Equal(77UL, Assert.IsType<Pong>(await AfterPings(peer)).Nonce);

        // At 499 seconds the session would long have closed had the Ping not counted as receiving. The session re-arms its
        // idle time when it next waits, which may be just after this test's clock moved, so the close is asserted after a
        // full idle period more rather than at an exact second.
        peer.Time.Advance(TimeSpan.FromSeconds(Limits.IdleSeconds - 1));
        Assert.False(reading.IsCompleted, "The session closed although it received a Ping less than 5 minutes ago.");
        peer.Time.Advance(TimeSpan.FromSeconds(Limits.IdleSeconds));
        Assert.Equal(ViolationCode.Timeout, (await reading)!.Code);
    }

    // Heartbeats sent while the clock jumped arrive first, as many as the session's checks found it quiet; the next
    // other message is what the test is about.
    private static async Task<MeshMessage?> AfterPings(RawPeer peer)
    {
        while (true)
        {
            MeshMessage? message = await peer.ReceiveAsync();
            if (message is not Ping)
                return message;
        }
    }
}
