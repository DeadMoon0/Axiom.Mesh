using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Protocol;
using LocalPairingMode = Axiom.Mesh.Local.PairingMode;
using LocalPairingState = Axiom.Mesh.Local.PairingState;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// The pairing shell end to end over two in-memory nodes (PAIR-SPEC.md; LOCAL-SPEC.md §6.2): the request rules, the SAS
/// both screens show, what a completed pairing leaves in each mode, a cancel, an altered code, and the pairing connection
/// that never carries an AMP/1 frame.
/// </summary>
public sealed class PairingTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // LOCAL-SPEC.md §6.2, PAIR-SPEC.md §6.2, §6.3.
    [Fact]
    public async Task ConfirmIsInvalidWhileWaiting_OkAndUnchangedWhileConfirming_ASecondPairingIsBusy_ExpiryAt10MinutesFails()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop"), None);
        LocalClient alice = await mesh["alice-laptop"].ConnectUserAsync("cli", None);
        LocalClient bob = await mesh["bob-desktop"].ConnectUserAsync("cli", None);
        LocalResult<Subscription<PairingChanged>> subscribed = await alice.SubscribeAsync<PairingChanged>(new Filter(), None);
        await using Subscription<PairingChanged> events = subscribed.Value;

        Pairing shown = (await alice.SendAsync(new StartPairing { Mode = LocalPairingMode.ShowUser }, None)).Value;
        Assert.Equal(LocalPairingState.Waiting, shown.State);
        Assert.StartsWith("amp1:user:10.0.0.1:41810:", shown.Code, StringComparison.Ordinal);
        Assert.Empty(shown.Sas);
        Assert.Equal((ulong)(mesh.Clock.GetUtcNow() + TimeSpan.FromMinutes(10)).ToUnixTimeMilliseconds(), shown.Expires);

        LocalResult<Pairing> early = await alice.SendAsync(new ConfirmPairing { PairingId = shown.PairingId }, None);
        Assert.Equal(Status.Invalid, early.Status);
        Assert.Equal(LocalPairingState.Waiting, (await alice.SendAsync(new GetPairing(), None)).Value.State);
        Assert.Equal(Status.Busy, (await alice.SendAsync(new StartPairing { Mode = LocalPairingMode.ShowUser }, None)).Status);

        Pairing entered = (await bob.SendAsync(new StartPairing { Mode = LocalPairingMode.JoinUser, Code = shown.Code }, None)).Value;
        Assert.Equal(LocalPairingState.Waiting, entered.State);
        await mesh.WaitForPairingAsync("alice-laptop", alice, p => p.State == LocalPairingState.Confirm, "Confirm", None);
        await mesh.WaitForPairingAsync("bob-desktop", bob, p => p.State == LocalPairingState.Confirm, "Confirm", None);

        Pairing confirming = (await alice.SendAsync(new ConfirmPairing { PairingId = shown.PairingId }, None)).Value;
        Assert.Equal(LocalPairingState.Confirming, confirming.State);
        LocalResult<Pairing> again = await alice.SendAsync(new ConfirmPairing { PairingId = shown.PairingId }, None);
        Assert.Equal(Status.Ok, again.Status);
        Assert.Equal(confirming, again.Value);
        Assert.Equal(Status.NotFound, (await alice.SendAsync(new ConfirmPairing { PairingId = shown.PairingId + 1 }, None)).Status);

        // One millisecond before the expiry nothing changed; at it, both sides fail. The clock moves in steps that the
        // local clients answer, so that their connections are never idle for long.
        TimeSpan step = TimeSpan.FromSeconds(50);
        for (int i = 0; i < 11; i++)
        {
            mesh.Clock.Advance(step);
            Assert.Equal(LocalPairingState.Confirming, (await alice.SendAsync(new GetPairing(), None)).Value.State);
            Assert.Equal(LocalPairingState.Confirm, (await bob.SendAsync(new GetPairing(), None)).Value.State);
        }

        mesh.Clock.Advance(TimeSpan.FromMinutes(10) - (11 * step) - TimeSpan.FromMilliseconds(1));
        Assert.Equal(LocalPairingState.Confirming, (await alice.SendAsync(new GetPairing(), None)).Value.State);
        mesh.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Pairing expired = await mesh.WaitForPairingAsync("alice-laptop", alice, p => p.State == LocalPairingState.Failed, "the expiry", None);
        Assert.Contains("xpired", expired.Failure, StringComparison.Ordinal);
        await mesh.WaitForPairingAsync("bob-desktop", bob, p => p.State == LocalPairingState.Failed, "the expiry", None);
        Assert.Equal(Status.NotFound, (await alice.SendAsync(new ConfirmPairing { PairingId = shown.PairingId }, None)).Status);
        Assert.False(mesh["alice-laptop"].Node.Trust.IsKnownUser(mesh["bob-desktop"].User));

        LocalPairingState[] seen = await TakeAsync(events, 4);
        Assert.Equal([LocalPairingState.Waiting, LocalPairingState.Confirm, LocalPairingState.Confirming, LocalPairingState.Failed], seen);
        Assert.True((await alice.SendAsync(new StartPairing { Mode = LocalPairingMode.ShowUser }, None)).IsOk);
    }

    // LOCAL-SPEC.md §6.2: the mode checks of StartPairing.
    [Fact]
    public async Task EachModeChecksWhatItNeeds_AndACodeOfTheWrongKindOrOfTheOwnUserIsInvalid()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "alice-phone") with { WithoutIdentity = ["alice-phone"] }, None);
        LocalClient laptop = await mesh["alice-laptop"].ConnectUserAsync("cli", None);
        LocalClient phone = await mesh["alice-phone"].ConnectUserAsync("cli", None);

        Assert.Equal(Status.NoIdentity, (await phone.SendAsync(new StartPairing { Mode = LocalPairingMode.ShowUser }, None)).Status);
        Assert.Equal(Status.NoIdentity, (await phone.SendAsync(new StartPairing { Mode = LocalPairingMode.AddDevice, Code = "x" }, None)).Status);
        Assert.Equal(Status.AlreadyExists, (await laptop.SendAsync(new StartPairing { Mode = LocalPairingMode.ShowDevice }, None)).Status);
        Assert.Equal(Status.UserKeyAbsent, (await laptop.SendAsync(new StartPairing { Mode = LocalPairingMode.AddDevice, Code = "x" }, None)).Status);
        Assert.Equal(Status.Invalid, (await laptop.SendAsync(new StartPairing { Mode = (LocalPairingMode)9 }, None)).Status);
        Assert.Equal(Status.Invalid, (await laptop.SendAsync(new StartPairing { Mode = LocalPairingMode.JoinUser, Code = "amp1:user:nonsense" }, None)).Status);

        Pairing device = (await phone.SendAsync(new StartPairing { Mode = LocalPairingMode.ShowDevice }, None)).Value;
        Assert.StartsWith("amp1:device:10.0.0.2:41810:", device.Code, StringComparison.Ordinal);
        LocalResult<Pairing> wrongKind = await laptop.SendAsync(new StartPairing { Mode = LocalPairingMode.JoinUser, Code = device.Code }, None);
        Assert.Equal(Status.Invalid, wrongKind.Status);
        Assert.Equal("code", wrongKind.Error!.Detail);

        // A user code shown by a device of the own user: enrolment, not pairing (a decision PAIR-SPEC.md leaves open).
        string ownCode = new PairingCode(Protocol.PairingMode.ShowUser, [new PairingAddress("10.0.0.2", 41810)], mesh["alice-phone"].Identity.Device.PublicKey).Text;
        LocalResult<Pairing> own = await laptop.SendAsync(new StartPairing { Mode = LocalPairingMode.JoinUser, Code = ownCode }, None);
        Assert.Equal(Status.Invalid, own.Status);
        Assert.Equal(Status.NotFound, (await laptop.SendAsync(new GetPairing(), None)).Status);
    }

    // PAIR-SPEC.md §8, §12: both screens show the same string, the one of both user keys and both TLS keys.
    [Fact]
    public async Task BothScreensShowTheSasOfBothUserKeysAndBothTlsKeys_AndItDiffersWhenEitherKeyIsSubstituted()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop"), None);
        TestIdentity alice = mesh["alice-laptop"].Identity;
        TestIdentity bob = mesh["bob-desktop"].Identity;
        PublicKey eve = TestIdentity.Of("eve-device").Device.PublicKey;

        string sas = await mesh.PairAsync("alice-laptop", "bob-desktop", None);

        Assert.Equal(Sas.Derive(alice.User.PublicKey, alice.Device.PublicKey, bob.User.PublicKey, bob.Device.PublicKey), sas);
        Assert.Matches("^[A-Z2-7]{4}-[A-Z2-7]{4}$", sas);
        string[] substituted =
        [
            Sas.Derive(eve, alice.Device.PublicKey, bob.User.PublicKey, bob.Device.PublicKey),
            Sas.Derive(alice.User.PublicKey, eve, bob.User.PublicKey, bob.Device.PublicKey),
            Sas.Derive(alice.User.PublicKey, alice.Device.PublicKey, eve, bob.Device.PublicKey),
            Sas.Derive(alice.User.PublicKey, alice.Device.PublicKey, bob.User.PublicKey, eve),
        ];
        Assert.DoesNotContain(sas, substituted);
    }

    // PAIR-SPEC.md §9.1: each side knows the other user and holds their Roster; then they establish under AMP/1.
    [Fact]
    public async Task AfterShowUserAndJoinUserEachKnowsTheOtherUser_AndTheirDevicesEstablishAnAmp1Session()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop"), None);
        TestNode alice = mesh["alice-laptop"];
        TestNode bob = mesh["bob-desktop"];
        Assert.False(alice.Node.Trust.IsKnownUser(bob.User));

        await mesh.PairAsync("alice-laptop", "bob-desktop", None);

        Assert.True(alice.Node.Trust.IsKnownUser(bob.User));
        Assert.True(bob.Node.Trust.IsKnownUser(alice.User));
        Assert.True(alice.Node.Trust.TryGetUser(bob.User, out UserTrust? held));
        Assert.Equal(1ul, held.Roster!.Content.Seq);
        await mesh.ConnectAsync("alice-laptop", "bob-desktop", None);
    }

    // PAIR-SPEC.md §9.2, §9.3: the device holds the user's identity (without the user key) and the Roster one seq higher
    // that both sides hold, and becomes established with the device that enrolled it.
    [Fact]
    public async Task AddDeviceEnrolsASecondDevice_ItHoldsTheUsersIdentityAndRosterSeqPlus1OnBoth_AndEstablishesWithTheFirst()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "alice-phone") with { WithoutIdentity = ["alice-phone"], Silent = ["alice-phone"] }, None);
        TestNode laptop = mesh["alice-laptop"];
        TestNode phone = mesh["alice-phone"];

        await mesh.EnrolAsync("alice-laptop", "alice-phone", None);

        LocalClient client = await phone.ConnectUserAsync("cli", None);
        Identity identity = (await client.SendAsync(new GetIdentity(), None)).Value;
        Assert.True(identity.Present);
        Assert.Equal(laptop.User, identity.UserId);
        Assert.Equal(phone.Device, identity.DeviceId);
        Assert.False(identity.UserKeyPresent);
        Assert.Equal(2ul, identity.RosterSeq);
        Assert.Equal("alice-phone", identity.DeviceName);
        Assert.True(laptop.Node.Trust.TryGetUser(laptop.User, out UserTrust? own));
        Assert.Equal(2ul, own.Roster!.Content.Seq);
        Assert.Null(await phone.Environment.Keys.ReadAsync(KeyRole.User, None));
        await mesh.ConnectAsync("alice-laptop", "alice-phone", None);
    }

    // PAIR-SPEC.md §6.2: a user cancels; the other side ends Cancelled too, and nothing is stored.
    [Fact]
    public async Task ACancelOnEitherSideEndsBothCancelled_AndNothingIsStored()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop"), None);
        LocalClient alice = await mesh["alice-laptop"].ConnectUserAsync("cli", None);
        LocalClient bob = await mesh["bob-desktop"].ConnectUserAsync("cli", None);
        Pairing shown = (await alice.SendAsync(new StartPairing { Mode = LocalPairingMode.ShowUser }, None)).Value;
        Pairing entered = (await bob.SendAsync(new StartPairing { Mode = LocalPairingMode.JoinUser, Code = shown.Code }, None)).Value;
        await mesh.WaitForPairingAsync("alice-laptop", alice, p => p.State == LocalPairingState.Confirm, "Confirm", None);
        await alice.SendAsync(new ConfirmPairing { PairingId = shown.PairingId }, None);

        Pairing cancelled = (await bob.SendAsync(new CancelPairing { PairingId = entered.PairingId }, None)).Value;

        Assert.Equal(LocalPairingState.Cancelled, cancelled.State);
        await mesh.WaitForPairingAsync("alice-laptop", alice, p => p.State == LocalPairingState.Cancelled, "Cancelled", None);
        Assert.Equal(Status.NotFound, (await bob.SendAsync(new CancelPairing { PairingId = entered.PairingId }, None)).Status);
        Assert.Equal(Status.NotFound, (await alice.SendAsync(new ConfirmPairing { PairingId = shown.PairingId }, None)).Status);
        Assert.False(mesh["alice-laptop"].Node.Trust.IsKnownUser(mesh["bob-desktop"].User));
        Assert.False(mesh["bob-desktop"].Node.Trust.IsKnownUser(mesh["alice-laptop"].User));
    }

    // PAIR-SPEC.md §4.2, §10: an altered code pins a key the showing device does not hold, so the entering side closes
    // the handshake, the pairing fails there, the showing side keeps waiting, and nobody learns anybody.
    [Fact]
    public async Task ACodeAlteredToEvesKeyIsRefusedAtTheHandshake_TheShowingSideKeepsWaiting_NothingIsStored()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop", "bob-desktop"), None);
        LocalClient alice = await mesh["alice-laptop"].ConnectUserAsync("cli", None);
        LocalClient bob = await mesh["bob-desktop"].ConnectUserAsync("cli", None);
        Pairing shown = (await alice.SendAsync(new StartPairing { Mode = LocalPairingMode.ShowUser }, None)).Value;
        Assert.True(PairingCode.TryParse(shown.Code, out PairingCode? code));
        string altered = new PairingCode(Protocol.PairingMode.ShowUser, code.Addresses, TestIdentity.Of("eve-device").Device.PublicKey).Text;

        await bob.SendAsync(new StartPairing { Mode = LocalPairingMode.JoinUser, Code = altered }, None);

        Pairing failed = await mesh.WaitForPairingAsync("bob-desktop", bob, p => p.State == LocalPairingState.Failed, "the refusal", None);
        Assert.Contains("no address of the code answered", failed.Failure, StringComparison.Ordinal);
        Assert.Equal(LocalPairingState.Waiting, (await alice.SendAsync(new GetPairing(), None)).Value.State);
        Assert.False(mesh["bob-desktop"].Node.Trust.IsKnownUser(mesh["alice-laptop"].User));
        Assert.False(mesh["alice-laptop"].Node.Trust.IsKnownUser(mesh["bob-desktop"].User));
        Assert.DoesNotContain(mesh["alice-laptop"].Node.Facts.Recent(), f => f.Kind == NodeFactKind.PairingChanged && f.Detail.Contains("Confirm", StringComparison.Ordinal));
    }

    // PAIR-SPEC.md §4.1, §5.1 (debt 22): the connection switches to the pairing protocol before any AMP/1 frame, says
    // PairHello first, sends nothing after the pairing ended, and a second connection is answered Busy.
    [Fact]
    public async Task APairingConnectionCarriesNoAmp1Frame_AndASecondOneIsAnsweredBusy()
    {
        await using TestMesh mesh = await TestMesh.StartAsync(TestMeshOptions.Of("alice-laptop"), None);
        TestNode alice = mesh["alice-laptop"];
        LocalClient client = await alice.ConnectUserAsync("cli", None);
        await client.SendAsync(new StartPairing { Mode = LocalPairingMode.ShowUser }, None);
        ScriptedPeerOptions pairing = ScriptedPeerOptions.Default with { Alpn = [PairProtocol.Alpn] };

        await using ScriptedPeer first = await ScriptedPeer.ConnectAsync(alice, TestIdentity.Of("eve-device"), pairing, None);
        PairHello hello = await first.ExpectPairAsync<PairHello>(None);
        Assert.Equal(Protocol.PairingMode.ShowUser, hello.Mode);
        Assert.Equal(alice.Device, hello.Device);
        Assert.Equal(alice.Identity.User.PublicKey, hello.UserKey);

        await using (ScriptedPeer second = await ScriptedPeer.ConnectAsync(alice, TestIdentity.Of("bob-desktop"), pairing with { From = new IPEndPoint(IPAddress.Parse("10.0.0.201"), 50000) }, None))
        {
            PairCancel busy = await second.ExpectPairAsync<PairCancel>(None);
            Assert.Equal(PairCancelReason.Busy, busy.Reason);
            await second.ExpectPairingClosedAsync(None);
        }

        await first.SendPairAsync(new PairCancel(PairCancelReason.Cancelled, "changed my mind"), None);
        await first.ExpectPairingClosedAsync(None);
        await mesh.WaitForPairingAsync("alice-laptop", client, p => p.State == LocalPairingState.Cancelled, "Cancelled", None);
    }

    private static async Task<LocalPairingState[]> TakeAsync(Subscription<PairingChanged> events, int count)
    {
        var states = new List<LocalPairingState>();
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        await foreach (PairingChanged changed in events.WithCancellation(deadline.Token))
        {
            states.Add(changed.Pairing.State);
            if (states.Count == count)
                break;
        }

        return [.. states];
    }
}
