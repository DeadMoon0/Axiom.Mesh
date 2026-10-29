using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Protocol.Tests.Support;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Errors;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>The session states of SPEC.md §6.1 and what each accepts, sends and answers (§6.4, §7, §12.1).</summary>
public sealed class SessionStateTests
{
    // §6.1: "Before the peer's Hello, a node accepts only Hello and Bye; any other frame is a violation. If no Hello
    // arrives within 10 seconds, the connection is closed."
    [Fact]
    public async Task BeforeThePeersHelloOnlyHelloAndByeAreAccepted_AndNoHelloWithin10SecondsCloses()
    {
        foreach (MeshMessage early in MessageSamples.All().Where(m => m is not (Hello or Bye)).DistinctBy(m => m.Id))
        {
            await using RawPeer peer = await RawPeer.OpenAsync();
            Task<Violation?> reading = peer.ReadUntilViolationAsync();
            await peer.SendAsync(early);

            Violation? violation = await reading;
            Assert.True(violation is { Code: ViolationCode.UnexpectedMessage }, $"{early} before Hello was not a violation.");
            Assert.Equal(ByeReason.Violation, Assert.IsType<Bye>(await peer.ReceiveAsync()).Reason);
        }

        await using (RawPeer peer = await RawPeer.OpenAsync())
        {
            await peer.SendAsync(MessageSamples.Hello());
            Assert.IsType<Hello>((await peer.Session.ReadAsync(CancellationToken.None)).Message);
            Assert.Equal(Amp.Established, peer.Session.State);
        }

        await using (RawPeer peer = await RawPeer.OpenAsync())
        {
            await peer.SendAsync(new Bye(ByeReason.Shutdown, "bye"));
            Assert.IsType<Bye>((await peer.Session.ReadAsync(CancellationToken.None)).Message);
            Assert.Equal(Amp.Closed, peer.Session.State);
        }

        await using (RawPeer peer = await RawPeer.OpenAsync())
        {
            Task<Violation?> reading = peer.ReadUntilViolationAsync();
            peer.Time.Advance(TimeSpan.FromSeconds(Limits.FirstHelloSeconds) - TimeSpan.FromMilliseconds(1));
            Assert.False(reading.IsCompleted);

            peer.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(ViolationCode.Timeout, (await reading)!.Code);
            Assert.Equal(ByeReason.Idle, Assert.IsType<Bye>(await peer.ReceiveAsync()).Reason);
        }
    }

    // §6.1: "Each side MUST send Hello as its first frame"; §7: "Established only" messages are refused before.
    [Fact]
    public async Task ANodeSendsHelloPingOrByeBeforeThePeersHello_AndNothingElse()
    {
        await using RawPeer peer = await RawPeer.OpenAsync();

        await Assert.ThrowsAsync<ProtocolStateException>(async () => await peer.Session.WriteAsync(new Want([]), CancellationToken.None));
        await peer.Session.WriteAsync(MessageSamples.Hello(), CancellationToken.None);
        await peer.Session.WriteAsync(new Ping(1), CancellationToken.None);

        Assert.IsType<Hello>(await peer.ReceiveAsync());
        Assert.IsType<Ping>(await peer.ReceiveAsync());
    }

    // §7.1 is sent once; §12.1 "a message in the wrong state" is a violation, and a second Hello is one.
    [Fact]
    public async Task ASecondHelloIsAViolation()
    {
        await using RawPeer peer = await RawPeer.OpenAsync();
        await peer.EstablishAsync();

        Task<Violation?> reading = peer.ReadUntilViolationAsync();
        await peer.SendAsync(MessageSamples.Hello());

        Assert.Equal(ViolationCode.UnexpectedMessage, (await reading)!.Code);
        Assert.Equal(ByeReason.Violation, Assert.IsType<Bye>(await peer.ReceiveAsync()).Reason);
    }

    // §6.3: "MUST answer Ping with Pong carrying the same nonce"; §7.10: phase 1 offers no service, so every ServiceOpen
    // is refused as unknown; §1.4: an unknown extension is read and discarded. None of these reaches the application.
    [Fact]
    public async Task OnceEstablishedPingIsAnsweredServiceOpenRefusedAndAnExtensionDiscarded()
    {
        await using RawPeer peer = await RawPeer.OpenAsync();
        await peer.EstablishAsync();
        Task<ProtocolReadResult<MeshMessage>> reading = peer.Session.ReadAsync(CancellationToken.None).AsTask();

        await peer.SendAsync(new Ping(42));
        Assert.Equal(42UL, Assert.IsType<Pong>(await peer.ReceiveAsync()).Nonce);

        await peer.SendAsync(new ServiceOpen("axiom-mesh-sync/1"));
        ServiceRefused refused = Assert.IsType<ServiceRefused>(await peer.ReceiveAsync());
        Assert.Equal(("axiom-mesh-sync/1", ServiceRefusal.Unknown), (refused.Service, refused.Reason));

        await peer.SendAsync(Frames.Raw(200, [9, 9, 9]));
        await peer.SendAsync(new Pong(7));
        await peer.SendAsync(new NotFound(MessageSamples.Hash(1)));

        Assert.IsType<NotFound>((await reading).Message);
    }

    // §6.4: "A node that receives Bye MUST NOT send further frames"; a session that sends Bye has closed.
    [Fact]
    public async Task AByeReceivedOrSentEndsTheSessionWithoutAnotherFrame()
    {
        await using (RawPeer peer = await RawPeer.OpenAsync())
        {
            await peer.EstablishAsync();
            await peer.SendAsync(new Bye(ByeReason.Duplicate, string.Empty));
            Assert.IsType<Bye>((await peer.Session.ReadAsync(CancellationToken.None)).Message);
            Assert.Equal(Amp.Closed, peer.Session.State);

            await peer.Session.CloseAsync(CancellationToken.None);
            await peer.Connection.DisposeAsync();
            Assert.Null(await peer.ReceiveAsync());
        }

        await using (RawPeer peer = await RawPeer.OpenAsync())
        {
            await peer.EstablishAsync();
            await peer.Session.WriteAsync(new Bye(ByeReason.Unauthorized, "removed"), CancellationToken.None);
            Assert.Equal(Amp.Closed, peer.Session.State);
            Assert.Equal(ByeReason.Unauthorized, Assert.IsType<Bye>(await peer.ReceiveAsync()).Reason);
        }
    }

    // §6.4: an orderly close is Bye; closing an established session sends Bye(Shutdown).
    [Fact]
    public async Task ClosingAnEstablishedSessionSendsByeShutdown()
    {
        await using RawPeer peer = await RawPeer.OpenAsync();
        await peer.EstablishAsync();

        Task closing = peer.Session.CloseAsync(CancellationToken.None).AsTask();
        Assert.Equal(ByeReason.Shutdown, Assert.IsType<Bye>(await peer.ReceiveAsync()).Reason);
        await peer.SendAsync(new Bye(ByeReason.Shutdown, string.Empty));
        await closing;
    }

    // §7.10: a phase-1 node never sends ServiceOpen, so an answer to one is a message in the wrong state (§12.1).
    [Fact]
    public async Task AServiceAnswerWithoutAServiceOpenIsAViolation()
    {
        await using RawPeer peer = await RawPeer.OpenAsync();
        await peer.EstablishAsync();

        Task<Violation?> reading = peer.ReadUntilViolationAsync();
        await peer.SendAsync(new ServiceAccepted("axiom-mesh-sync/1"));

        Assert.Equal(ViolationCode.UnexpectedMessage, (await reading)!.Code);
    }
}
