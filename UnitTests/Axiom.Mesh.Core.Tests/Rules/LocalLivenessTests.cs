using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// LOCAL-SPEC.md §4.4 and §1.6 decision 11: the Host pings a client it received nothing from for 60 seconds; a client
/// that answers within 30 seconds is never closed for being quiet, one that does not is closed with
/// <c>Bye(Unresponsive)</c>.
/// </summary>
public sealed class LocalLivenessTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData(0, null, "Wait")] // just heard from: wait
    [InlineData(59, null, "Wait")] // 59 s of silence: wait
    [InlineData(60, null, "Ping")] // 60 s of silence: Ping (§4.4)
    [InlineData(90, 29, "Wait")] // the Pong may still come within 30 s
    [InlineData(90, 30, "Unresponsive")] // 30 s without the Pong: Bye(Unresponsive)
    public void PingAfter60SecondsOfSilence_UnresponsiveWhenThePongIs30SecondsLate(int silent, int? pingAge, string expected)
    {
        TimeSpan? age = pingAge is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
        Assert.Equal(expected, LivenessRule.Decide(TimeSpan.FromSeconds(silent), age).Action.ToString());
    }

    [Fact]
    public async Task AClientThatAnswersPingIsNeverClosedForBeingQuiet_NoPongWithin30SecondsIsByeUnresponsive()
    {
        await using TestMesh mesh = await LocalMeshes.StartAsync();
        TestNode node = mesh[LocalMeshes.Node];

        // A quiet client whose library answers every Ping: five minutes on the fake clock, a minute at a time.
        LocalClient quiet = await node.ConnectUserAsync("quiet", None);
        LocalConnection answering = Assert.Single(node.Node.Local.Book.Live());
        for (int minute = 1; minute <= 5; minute++)
        {
            mesh.Clock.Advance(TimeSpan.FromSeconds(Limits.LocalPingAfterSeconds));
            int answered = minute;
            await Waits.UntilAsync(() => answering.Pongs == answered && !answering.PingOutstanding, $"the Pong of minute {minute}");
        }

        Assert.False(quiet.Closed.IsCompleted);
        Assert.True((await quiet.SendAsync(new GetIdentity(), None)).IsOk);
        await quiet.ByeAsync(None);
        await Waits.UntilAsync(() => node.Node.Local.Connections == 0, "the quiet client gone");

        // A client that reads nothing, so its Ping is never answered: 60 seconds, then 30 more.
        await using RawLocalClient deaf = await RawLocalClient.ReadyAsync(mesh, LocalMeshes.Node);
        LocalConnection silent = Assert.Single(node.Node.Local.Book.Live());
        mesh.Clock.Advance(TimeSpan.FromSeconds(Limits.LocalPingAfterSeconds));
        await Waits.UntilAsync(() => silent.PingOutstanding, "the Ping");
        mesh.Clock.Advance(TimeSpan.FromSeconds(Limits.LocalPongSeconds - 1));
        Assert.Equal(1, node.Node.Local.Connections);
        mesh.Clock.Advance(TimeSpan.FromSeconds(1));

        Bye? bye = await deaf.EndAsync();
        Assert.Equal(ByeReason.Unresponsive, bye?.Reason);
    }
}
