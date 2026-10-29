using System;
using System.Collections.Generic;
using Axiom.Mesh.Protocol.Tests.Support;
using Axiom.Mesh.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>The checks of PAIR-SPEC.md §7: the other side's <c>PairHello</c>, and the Roster that enrols a device.</summary>
public sealed class PairingRulesTests
{
    private static readonly HashSet<DeviceId> NoneRemoved = [];

    // PAIR-SPEC §7.1: a user side's PairHello is accepted with its Roster verified under its own user key and listing its
    // device; the device being enrolled sends neither.
    [Fact]
    public void APairHelloIsAcceptedWithItsVerifiedRoster()
    {
        Assert.True(PairingRules.TryAcceptHello(PairingSamples.BobJoins, TestKeys.BobDesktop.PublicKey, PairingMode.ShowUser, out PairPeer? bob, out _));
        Assert.Equal(TestKeys.Bob.PublicKey, bob.UserKey);
        Assert.Equal(PairingSamples.BobRoster.Hash, bob.Roster!.Hash);
        Assert.Equal(TestKeys.BobDesktop.PublicKey.AsDeviceId(), bob.Device);
        Assert.Equal("Bob", bob.Name);

        Assert.True(PairingRules.TryAcceptHello(PairingSamples.NewDeviceShows, TestKeys.EveDevice.PublicKey, PairingMode.AddDevice, out PairPeer? device, out _));
        Assert.Null(device.Roster);
        Assert.Null(device.UserKey);
    }

    // PAIR-SPEC §7.1: every failed check ends the pairing with PairCancel(Refused).
    [Theory]
    // a version other than 1
    [InlineData("version")]
    // JoinUser does not pair with JoinUser
    [InlineData("mode")]
    // the PairHello names a device that is not H(TLS key)
    [InlineData("device")]
    // Bob's PairHello carrying Alice's Roster
    [InlineData("roster of another user")]
    // Alice's removed phone
    [InlineData("device not current")]
    // one flipped bit in the Roster
    [InlineData("roster invalid")]
    // a ShowDevice side that sends a user key
    [InlineData("device with a user")]
    public void APairHelloThatFailsACheckIsRefused(string failure)
    {
        (PairHello hello, PublicKey tlsKey, PairingMode own) = failure switch
        {
            "version" => (Reread(PairingSamples.BobJoins, body => body[1] = 2), TestKeys.BobDesktop.PublicKey, PairingMode.ShowUser),
            "mode" => (PairingSamples.BobJoins, TestKeys.BobDesktop.PublicKey, PairingMode.JoinUser),
            "device" => (PairingSamples.BobJoins, TestKeys.AliceLaptop.PublicKey, PairingMode.ShowUser),
            "roster of another user" => (new PairHello(PairingMode.JoinUser, TestKeys.BobDesktop.PublicKey.AsDeviceId(), "Bob", TestKeys.Bob.PublicKey, PairingSamples.AliceRoster.Bytes.Span), TestKeys.BobDesktop.PublicKey, PairingMode.ShowUser),
            "device not current" => (new PairHello(PairingMode.JoinUser, TestKeys.AlicePhone.PublicKey.AsDeviceId(), "Alice", TestKeys.Alice.PublicKey, PairingSamples.AliceRoster.Bytes.Span), TestKeys.AlicePhone.PublicKey, PairingMode.ShowUser),
            "roster invalid" => (Reread(PairingSamples.BobJoins, body => body[^10] ^= 1), TestKeys.BobDesktop.PublicKey, PairingMode.ShowUser),
            "device with a user" => (Reread(PairingSamples.AliceShows, body => body[2] = (byte)PairingMode.ShowDevice), TestKeys.AliceLaptop.PublicKey, PairingMode.AddDevice),
            _ => throw new ArgumentException(failure, nameof(failure)),
        };

        Assert.False(PairingRules.TryAcceptHello(hello, tlsKey, own, out _, out PairCancel? refusal));
        Assert.Equal(PairCancelReason.Refused, refusal.Reason);
        Assert.NotEmpty(refusal.Detail);
    }

    // PAIR-SPEC §7.2: the device adopts a Roster only when it is the user's, newer than the one the user side showed, and
    // lists this device's key as current.
    [Fact]
    public void AnEnrolmentIsAcceptedOnlyAsTheUsersNewerRosterListingThisDevice()
    {
        Assert.True(PairingRules.TryAcceptHello(PairingSamples.NewDeviceShows, TestKeys.EveDevice.PublicKey, PairingMode.AddDevice, out PairPeer? device, out _));
        Assert.True(PairingRules.TryAcceptHello(PairingSamples.AliceAdds, TestKeys.AliceLaptop.PublicKey, PairingMode.ShowDevice, out PairPeer? alice, out _));
        Assert.True(PairingRules.TryEnrol(PairingSamples.AliceRoster, device, NoneRemoved, 1_800_000_000_000, out RosterDraft? draft));
        VerifiedRoster enrolling = draft.Sign(TestKeys.Alice);

        Assert.True(PairingRules.TryAcceptEnrolment(PairConfirm.Enrolling(enrolling), alice, TestKeys.EveDevice.PublicKey, out VerifiedRoster? adopted, out _));
        Assert.Equal(PairingSamples.AliceRoster.Content.Seq + 1, adopted.Content.Seq);
        Assert.True(adopted.Content.TryGetDevice(TestKeys.EveDevice.PublicKey.AsDeviceId(), out RosterDevice? listed));
        Assert.Equal("workshop-pc", listed.Name);

        VerifiedRoster withoutDevice = new RosterDraft { Seq = 9, Devices = [new RosterDevice(TestKeys.AliceLaptop.PublicKey, "laptop", 1, removed: false)] }.Sign(TestKeys.Alice);
        VerifiedRoster othersRoster = new RosterDraft { Seq = 9, Devices = [new RosterDevice(TestKeys.EveDevice.PublicKey, "d", 1, removed: false)] }.Sign(TestKeys.Bob);
        Assert.False(PairingRules.TryAcceptEnrolment(PairConfirm.Plain, alice, TestKeys.EveDevice.PublicKey, out _, out _));
        Assert.False(PairingRules.TryAcceptEnrolment(PairConfirm.Enrolling(PairingSamples.AliceRoster), alice, TestKeys.EveDevice.PublicKey, out _, out _));
        Assert.False(PairingRules.TryAcceptEnrolment(PairConfirm.Enrolling(withoutDevice), alice, TestKeys.EveDevice.PublicKey, out _, out _));
        Assert.False(PairingRules.TryAcceptEnrolment(PairConfirm.Enrolling(othersRoster), alice, TestKeys.EveDevice.PublicKey, out _, out PairCancel? refusal));
        Assert.Equal(PairCancelReason.Refused, refusal.Reason);
    }

    // SPEC §8.8: removal is permanent, so a device removed (or omitted) before is never enrolled again.
    [Fact]
    public void ADeviceOnceRemovedIsNeverEnrolledAgain_ACurrentOneIsKept()
    {
        PairHello phone = new(PairingMode.ShowDevice, TestKeys.AlicePhone.PublicKey.AsDeviceId(), "phone", null, ReadOnlySpan<byte>.Empty);
        Assert.True(PairingRules.TryAcceptHello(phone, TestKeys.AlicePhone.PublicKey, PairingMode.AddDevice, out PairPeer? removed, out _));
        Assert.False(PairingRules.TryEnrol(PairingSamples.AliceRoster, removed, NoneRemoved, 0, out _));

        Assert.True(PairingRules.TryAcceptHello(PairingSamples.NewDeviceShows, TestKeys.EveDevice.PublicKey, PairingMode.AddDevice, out PairPeer? device, out _));
        Assert.False(PairingRules.TryEnrol(PairingSamples.AliceRoster, device, new HashSet<DeviceId> { device.Device }, 0, out _));

        PairHello laptop = new(PairingMode.ShowDevice, TestKeys.AliceLaptop.PublicKey.AsDeviceId(), "laptop", null, ReadOnlySpan<byte>.Empty);
        Assert.True(PairingRules.TryAcceptHello(laptop, TestKeys.AliceLaptop.PublicKey, PairingMode.AddDevice, out PairPeer? current, out _));
        Assert.True(PairingRules.TryEnrol(PairingSamples.AliceRoster, current, NoneRemoved, 0, out RosterDraft? kept));
        Assert.Equal(PairingSamples.AliceRoster.Content.Devices.Count, kept.Devices.Count);
    }

    // A PairHello as a peer could send it: encoded, changed by hand, read back.
    private static PairHello Reread(PairHello hello, Action<byte[]> change)
    {
        byte[] body = Frames.Encode(PairProtocol.Definition, hello)[6..];
        change(body);
        var reader = new WireReader(body);
        Assert.True(PairHello.TryRead(ref reader, out PairHello reread));
        return reread;
    }
}
