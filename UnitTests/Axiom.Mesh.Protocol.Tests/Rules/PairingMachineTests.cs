using System;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>The pairing state machine (PAIR-SPEC.md §6; LOCAL-SPEC.md §6.2).</summary>
public sealed class PairingMachineTests
{
    private const string AnySas = "K7QD-M2XA";
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    // LOCAL §6.2: "ConfirmPairing is Invalid in state Waiting, and in state Confirming it is OK and changes nothing";
    // "Exactly one pairing runs on a Host at a time; a second StartPairing fails with Busy"; "A pairing expires 10 minutes
    // after StartPairing, which is a failure."
    [Fact]
    public void ConfirmIsInvalidWhileWaiting_OkAndUnchangedWhileConfirming_ASecondPairingIsBusy_ExpiryAt10MinutesFails()
    {
        PairingMachine waiting = Start(PairingMode.ShowUser);
        PairingStep early = waiting.OnConfirm(T0);
        Assert.Equal(PairingResult.Invalid, early.Result);
        Assert.Same(waiting, early.Next);

        PairingMachine confirming = waiting.OnPeerHello(AnySas, T0).Next.OnConfirm(T0).Next;
        Assert.Equal(PairingState.Confirming, confirming.State);
        PairingStep again = confirming.OnConfirm(T0.AddMinutes(1));
        Assert.Equal((PairingResult.Ok, PairingAction.None), (again.Result, again.Action));
        Assert.Same(confirming, again.Next);

        Assert.False(PairingMachine.TryStart(confirming, PairingMode.JoinUser, T0.AddMinutes(9), out _));
        Assert.True(PairingMachine.TryStart(confirming, PairingMode.JoinUser, T0.AddMinutes(10), out _));

        Assert.Equal(T0.AddMinutes(10), confirming.Expires);
        Assert.Equal(PairingState.Confirming, confirming.OnTick(T0.AddMinutes(10).AddTicks(-1)).Next.State);
        PairingStep expired = confirming.OnTick(T0.AddMinutes(10));
        Assert.Equal(PairingState.Failed, expired.Next.State);
        Assert.Equal(PairingAction.SendCancel, expired.Action);
        Assert.Equal(PairCancelReason.Expired, expired.Cancel!.Reason);
        Assert.Equal(PairingResult.NotFound, expired.Next.OnConfirm(T0.AddMinutes(11)).Result);
        Assert.True(PairingMachine.TryStart(expired.Next, PairingMode.ShowUser, T0.AddMinutes(10), out _));
    }

    // PAIR-SPEC §6: in user modes each side sends PairConfirm when its user confirms, in either order; Paired once both did.
    [Fact]
    public void InUserModesEachSideConfirmsInEitherOrderAndIsPairedOnceBothDid()
    {
        PairingStep first = Start(PairingMode.ShowUser).OnPeerHello(AnySas, T0).Next.OnConfirm(T0);
        Assert.Equal((PairingState.Confirming, PairingAction.SendConfirm), (first.Next.State, first.Action));
        Assert.Equal(AnySas, first.Next.Sas);
        Assert.Equal(PairingState.Paired, first.Next.OnPeerConfirm(T0).Next.State);

        PairingMachine peerFirst = Start(PairingMode.JoinUser).OnPeerHello(AnySas, T0).Next.OnPeerConfirm(T0).Next;
        Assert.Equal(PairingState.Confirm, peerFirst.State);
        Assert.True(peerFirst.PeerConfirmed);
        PairingStep second = peerFirst.OnConfirm(T0);
        Assert.Equal((PairingState.Paired, PairingAction.SendConfirm), (second.Next.State, second.Action));
    }

    // PAIR-SPEC §6: the AddDevice side sends the enrolling Roster only after the device's user confirmed; an enrolment
    // that arrives before the device confirmed fails the pairing.
    [Fact]
    public void TheUserSideEnrolsTheDeviceOnlyAfterTheDevicesUserConfirmed()
    {
        PairingStep userFirst = Start(PairingMode.AddDevice).OnPeerHello(AnySas, T0).Next.OnConfirm(T0);
        Assert.Equal((PairingState.Confirming, PairingAction.None), (userFirst.Next.State, userFirst.Action));
        PairingStep enrol = userFirst.Next.OnPeerConfirm(T0);
        Assert.Equal((PairingState.Paired, PairingAction.SendEnrolment), (enrol.Next.State, enrol.Action));

        PairingMachine deviceConfirmedFirst = Start(PairingMode.AddDevice).OnPeerHello(AnySas, T0).Next.OnPeerConfirm(T0).Next;
        PairingStep enrolNow = deviceConfirmedFirst.OnConfirm(T0);
        Assert.Equal((PairingState.Paired, PairingAction.SendEnrolment), (enrolNow.Next.State, enrolNow.Action));

        PairingStep device = Start(PairingMode.ShowDevice).OnPeerHello(AnySas, T0).Next.OnConfirm(T0);
        Assert.Equal((PairingState.Confirming, PairingAction.SendConfirm), (device.Next.State, device.Action));
        Assert.Equal(PairingState.Paired, device.Next.OnPeerConfirm(T0).Next.State);

        PairingStep tooEarly = Start(PairingMode.ShowDevice).OnPeerHello(AnySas, T0).Next.OnPeerConfirm(T0);
        Assert.Equal(PairingState.Failed, tooEarly.Next.State);
        Assert.Equal(PairCancelReason.Violation, tooEarly.Cancel!.Reason);
    }

    // LOCAL §6.2: a pairing ends Paired, Failed or Cancelled; CancelPairing on an ended one is NotFound.
    [Fact]
    public void ACancelOnEitherSideEndsThePairing_AFailedCheckOrALostConnectionFailsIt()
    {
        PairingStep cancelled = Start(PairingMode.ShowUser).OnCancel(T0);
        Assert.Equal((PairingState.Cancelled, PairingAction.SendCancel, PairCancelReason.Cancelled), (cancelled.Next.State, cancelled.Action, cancelled.Cancel!.Reason));
        Assert.Equal(PairingResult.NotFound, cancelled.Next.OnCancel(T0).Result);

        PairingMachine confirm = Start(PairingMode.JoinUser).OnPeerHello(AnySas, T0).Next;
        Assert.Equal(PairingState.Cancelled, confirm.OnPeerCancel(new PairCancel(PairCancelReason.Cancelled, string.Empty), T0).Next.State);
        PairingMachine peerFailed = confirm.OnPeerCancel(new PairCancel(PairCancelReason.Refused, "roster"), T0).Next;
        Assert.Equal(PairingState.Failed, peerFailed.State);
        Assert.Contains("roster", peerFailed.Failure, StringComparison.Ordinal);

        PairingStep refused = Start(PairingMode.ShowUser).OnPeerRefused(new PairCancel(PairCancelReason.Refused, "deviceId is not H(TLS key)"), T0);
        Assert.Equal((PairingState.Failed, PairingAction.SendCancel), (refused.Next.State, refused.Action));

        Assert.Equal(PairingState.Waiting, Start(PairingMode.ShowDevice).OnDisconnected(T0).Next.State);
        Assert.Equal(PairingState.Failed, Start(PairingMode.JoinUser).OnDisconnected(T0).Next.State);
        Assert.Equal(PairingState.Failed, confirm.OnDisconnected(T0).Next.State);
        Assert.Equal(PairingResult.Invalid, confirm.OnPeerHello(AnySas, T0).Result);
    }

    private static PairingMachine Start(PairingMode mode)
    {
        Assert.True(PairingMachine.TryStart(null, mode, T0, out PairingMachine? started));
        Assert.Equal((PairingState.Waiting, mode), (started.State, started.Mode));
        Assert.True(started.AwaitsPeer);
        return started;
    }
}
