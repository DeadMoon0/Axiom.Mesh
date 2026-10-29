using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Tests.Support;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Testing;

namespace Axiom.Mesh.Protocol.Tests.Regression;

/// <summary>Two nodes over an in-memory transport, each running <see cref="Amp.Definition"/>, establish and close a session.</summary>
public sealed class HelloExchangeTests
{
    // Damage: a definition whose states disagree with themselves (Hello sendable but not receivable, a reply that is not
    // sendable, a close that sends after Bye) passes every codec test and fails the first real connection.
    [Fact]
    public async Task TwoNodesExchangeHelloCheckItEstablishTalkAndClose()
    {
        TransportPair pair = InMemoryTransport.CreatePair();
        await using Connection laptopConnection = Connection.FromPipe(pair.Client);
        await using Connection desktopConnection = Connection.FromPipe(pair.Server);
        Session<MeshMessage, MeshMessage> laptop = await laptopConnection.OpenAsync(Amp.Definition, CancellationToken.None);
        Session<MeshMessage, MeshMessage> desktop = await desktopConnection.OpenAsync(Amp.Definition, CancellationToken.None);

        // Alice's laptop and Bob's desktop know each other's users: each holds the other's Roster.
        VerifiedRoster alice = new RosterDraft { Seq = 2, Devices = [new RosterDevice(TestKeys.AliceLaptop.PublicKey, "laptop", 0, removed: false)] }.Sign(TestKeys.Alice);
        VerifiedRoster bob = new RosterDraft { Seq = 1, Devices = [new RosterDevice(TestKeys.BobDesktop.PublicKey, "desktop", 0, removed: false)] }.Sign(TestKeys.Bob);
        var trust = new TrustView().With(alice).With(bob);

        // §6.1: each side sends Hello first, without waiting for the other's.
        await laptop.WriteAsync(new Hello(TestKeys.AliceLaptop.PublicKey.AsDeviceId(), alice.Content.User, 2, Capabilities.FullMirror, []), CancellationToken.None);
        await desktop.WriteAsync(new Hello(TestKeys.BobDesktop.PublicKey.AsDeviceId(), bob.Content.User, 1, Capabilities.None, []), CancellationToken.None);

        Hello fromDesktop = Assert.IsType<Hello>((await laptop.ReadAsync(CancellationToken.None)).Message);
        Hello fromLaptop = Assert.IsType<Hello>((await desktop.ReadAsync(CancellationToken.None)).Message);
        Assert.IsType<HelloOutcome.Ok>(HelloRule.Check(fromDesktop, new PeerIdentityFacts(TestKeys.BobDesktop.PublicKey, 1), trust));
        Assert.IsType<HelloOutcome.Ok>(HelloRule.Check(fromLaptop, new PeerIdentityFacts(TestKeys.AliceLaptop.PublicKey, 2), trust));
        Assert.Equal(Amp.Established, laptop.State);
        Assert.Equal(Amp.Established, desktop.State);

        // Established: a Want arrives as sent, a Ping is answered by the definition alone.
        await laptop.WriteAsync(new Want([ObjectHash.Of([1, 2, 3])]), CancellationToken.None);
        Want want = Assert.IsType<Want>((await desktop.ReadAsync(CancellationToken.None)).Message);
        Assert.Equal(ObjectHash.Of([1, 2, 3]), Assert.Single(want.Hashes));

        await desktop.WriteAsync(new Ping(5), CancellationToken.None);
        Task<ProtocolReadResult<MeshMessage>> laptopReads = laptop.ReadAsync(CancellationToken.None).AsTask();
        await desktop.WriteAsync(new NotFound(ObjectHash.Of([1, 2, 3])), CancellationToken.None);
        Assert.IsType<NotFound>((await laptopReads).Message);

        // §6.4: the desktop closes with Bye(Shutdown); the laptop receives it and the session ends on both sides.
        Task desktopCloses = desktop.CloseAsync(CancellationToken.None).AsTask();
        Assert.Equal(ByeReason.Shutdown, Assert.IsType<Bye>((await laptop.ReadAsync(CancellationToken.None)).Message).Reason);
        await desktopCloses;
        Assert.Equal(Amp.Closed, laptop.State);
        Assert.Equal(Amp.Closed, desktop.State);
    }
}
