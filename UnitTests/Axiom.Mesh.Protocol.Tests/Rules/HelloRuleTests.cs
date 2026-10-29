using System;
using Axiom.Mesh.Protocol.Tests.Support;
using Axiom.Mesh.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>The checks of a peer's <c>Hello</c> (SPEC.md §7.1) and the Roster exchange that follows it.</summary>
public sealed class HelloRuleTests
{
    // Alice's Roster lists her laptop and her removed phone; Bob's lists his desktop. Eve is known to nobody.
    private static readonly TrustView Trust = new TrustView()
        .With(new RosterDraft
        {
            Seq = 2,
            Devices = [new RosterDevice(TestKeys.AliceLaptop.PublicKey, "laptop", 0, removed: false), new RosterDevice(TestKeys.AlicePhone.PublicKey, "phone", 0, removed: true)],
        }.Sign(TestKeys.Alice))
        .With(new RosterDraft { Seq = 5, Devices = [new RosterDevice(TestKeys.BobDesktop.PublicKey, "desktop", 0, removed: false)] }.Sign(TestKeys.Bob));

    // §7.1: "On receipt a node MUST check: version is 1; deviceId equals H(K) for the peer's TLS public key K; userId is
    // a known user; deviceId is a device of userId, not removed, in the current roster the node holds for that user. A
    // failing check is a violation and the connection is closed with Bye(Unauthorized)."
    [Theory]
    // version 2 from Alice's laptop
    [InlineData("version")]
    // the laptop's TLS key, but the Hello names Alice's phone
    [InlineData("device is not H(TLS key)")]
    // Eve's device, in nobody's roster
    [InlineData("unknown user")]
    // Alice's laptop claiming to be Bob's
    [InlineData("device of another user")]
    // Alice's phone, removed from her roster
    [InlineData("removed device")]
    public void AHelloThatFailsACheckIsByeUnauthorized(string failure)
    {
        (Hello hello, PublicKey tlsKey) = failure switch
        {
            "version" => (HelloOf(2, TestKeys.AliceLaptop, TestKeys.Alice), TestKeys.AliceLaptop.PublicKey),
            "device is not H(TLS key)" => (HelloOf(1, TestKeys.AlicePhone, TestKeys.Alice), TestKeys.AliceLaptop.PublicKey),
            "unknown user" => (HelloOf(1, TestKeys.EveDevice, TestKeys.Eve), TestKeys.EveDevice.PublicKey),
            "device of another user" => (HelloOf(1, TestKeys.AliceLaptop, TestKeys.Bob), TestKeys.AliceLaptop.PublicKey),
            "removed device" => (HelloOf(1, TestKeys.AlicePhone, TestKeys.Alice), TestKeys.AlicePhone.PublicKey),
            _ => throw new ArgumentException(failure, nameof(failure)),
        };

        HelloOutcome outcome = HelloRule.Check(hello, new PeerIdentityFacts(tlsKey, HeldRosterSeq: 2), Trust);

        HelloOutcome.Refused refused = Assert.IsType<HelloOutcome.Refused>(outcome);
        Assert.Equal(ByeReason.Unauthorized, refused.Bye.Reason);
        Assert.NotEmpty(refused.Bye.Detail);
    }

    // §7.1: "if the peer's rosterSeq is lower than the sequence number of the roster this node holds for that user, the
    // node MUST send that Roster object ... If it is higher, the node MUST send RosterQuery(userId)."
    [Theory]
    // the peer holds an older Roster of its own user
    [InlineData(1UL, RosterFollowUp.SendHeldRoster)]
    // the same
    [InlineData(2UL, RosterFollowUp.None)]
    // a newer one
    [InlineData(3UL, RosterFollowUp.QueryRoster)]
    public void AValidHelloEstablishes_AndTheRosterSeqDecidesWhatFollows(ulong peerRosterSeq, RosterFollowUp expected)
    {
        var hello = new Hello(TestKeys.AliceLaptop.PublicKey.AsDeviceId(), TestKeys.Alice.PublicKey.AsUserId(), peerRosterSeq, Capabilities.None, []);

        HelloOutcome outcome = HelloRule.Check(hello, new PeerIdentityFacts(TestKeys.AliceLaptop.PublicKey, HeldRosterSeq: 2), Trust);

        HelloOutcome.Ok ok = Assert.IsType<HelloOutcome.Ok>(outcome);
        Assert.Equal(expected, ok.FollowUp);
        Assert.Equal(TestKeys.Alice.PublicKey.AsUserId(), ok.Peer.User);
        Assert.False(ok.Peer.IsRemoved);
    }

    // A Hello of another version cannot be made by the constructor; a peer can send one, so it is read from bytes.
    private static Hello HelloOf(ushort version, EcdsaSigner device, EcdsaSigner user)
    {
        byte[] body = Frames.Body(new Hello(device.PublicKey.AsDeviceId(), user.PublicKey.AsUserId(), 2, Capabilities.None, []));
        body[0] = (byte)(version >> 8);
        body[1] = (byte)version;
        var reader = new WireReader(body);
        Assert.True(Hello.TryRead(ref reader, out Hello hello));
        return hello;
    }
}
