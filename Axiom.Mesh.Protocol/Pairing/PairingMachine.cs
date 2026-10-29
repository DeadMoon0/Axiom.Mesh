using System;
using System.Diagnostics.CodeAnalysis;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// One pairing on one side, as a pure state machine (PAIR-SPEC.md §6; LOCAL-SPEC.md §6.2): Waiting → Confirm →
/// Confirming → Paired, or Failed or Cancelled, expiring 10 minutes after it started. Immutable: every event returns a
/// <see cref="PairingStep"/> with the next machine and what to send. It takes timestamps and reads no clock; the shell
/// feeds it the events of the session, the user's commands and a timer at <see cref="Expires"/>.
/// </summary>
/// <remarks>
/// The users confirm in any order, except in device modes: the <see cref="PairingMode.AddDevice"/> side sends its
/// confirmation, which carries the Roster that enrols the device, only after the device's user confirmed, so a device
/// never holds a Roster listing it unless its own user agreed.
/// </remarks>
public sealed class PairingMachine
{
    private PairingMachine(PairingMode mode, PairingState state, DateTimeOffset expires, string sas, bool peerConfirmed, string failure)
    {
        Mode = mode;
        State = state;
        Expires = expires;
        Sas = sas;
        PeerConfirmed = peerConfirmed;
        Failure = failure;
    }

    /// <summary>This side's mode.</summary>
    public PairingMode Mode { get; }

    /// <summary>The state.</summary>
    public PairingState State { get; }

    /// <summary>When the pairing fails if it has not ended: 10 minutes after it started.</summary>
    public DateTimeOffset Expires { get; }

    /// <summary>The short authentication string the users compare, from <see cref="PairingState.Confirm"/> on; empty before.</summary>
    public string Sas { get; }

    /// <summary>Whether the other side's <c>PairConfirm</c> arrived.</summary>
    public bool PeerConfirmed { get; }

    /// <summary>Why the pairing failed, in <see cref="PairingState.Failed"/>; empty otherwise.</summary>
    public string Failure { get; }

    /// <summary>Whether the pairing has ended: <see cref="PairingState.Paired"/>, <see cref="PairingState.Failed"/> or <see cref="PairingState.Cancelled"/>.</summary>
    public bool IsEnded => State is PairingState.Paired or PairingState.Failed or PairingState.Cancelled;

    /// <summary>Whether a pairing connection may be taken now: this side waits for the other.</summary>
    public bool AwaitsPeer => State == PairingState.Waiting;

    /// <summary>Whether the pairing still runs at <paramref name="now"/>; while it does, no other may start.</summary>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when it has not ended and not expired.</returns>
    public bool IsActive(DateTimeOffset now) => !IsEnded && now < Expires;

    /// <summary>Starts a pairing, unless one is still active: one at a time per Host (LOCAL-SPEC.md §6.2, §12).</summary>
    /// <param name="current">The current or last pairing, if any.</param>
    /// <param name="mode">This side's mode.</param>
    /// <param name="now">The current time.</param>
    /// <param name="started">The new pairing in <see cref="PairingState.Waiting"/>; or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when <paramref name="current"/> is still active: the answer is <c>Busy</c>.</returns>
    /// <exception cref="ArgumentException">The mode is not one of the four.</exception>
    public static bool TryStart(PairingMachine? current, PairingMode mode, DateTimeOffset now, [NotNullWhen(true)] out PairingMachine? started)
    {
        if (mode is < PairingMode.ShowUser or > PairingMode.AddDevice)
            throw new ArgumentException($"Mode {(byte)mode} is not a pairing mode (LOCAL-SPEC.md §14.10).", nameof(mode));

        started = current is not null && current.IsActive(now)
            ? null
            : new PairingMachine(mode, PairingState.Waiting, now + Lifetime, string.Empty, peerConfirmed: false, string.Empty);
        return started is not null;
    }

    /// <summary>The other side's <c>PairHello</c> passed <see cref="PairingRules.TryAcceptHello"/>: both are connected.</summary>
    /// <param name="sas">The string <see cref="Protocol.Sas.Derive"/> gave for both sides' keys.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Waiting becomes Confirm; anything else is <see cref="PairingResult.Invalid"/> and ignored.</returns>
    public PairingStep OnPeerHello(string sas, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(sas);
        if (Expired(now, out PairingStep? expired))
            return expired;
        return State == PairingState.Waiting
            ? Step(PairingResult.Ok, With(PairingState.Confirm, sas: sas))
            : Ignored();
    }

    /// <summary>
    /// A check of what the other side sent failed (<see cref="PairingRules.TryAcceptHello"/> or
    /// <see cref="PairingRules.TryAcceptEnrolment"/>): the pairing fails and the refusal is sent.
    /// </summary>
    /// <param name="refusal">The refusal the check produced.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Failed, sending <paramref name="refusal"/>.</returns>
    public PairingStep OnPeerRefused(PairCancel refusal, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        if (Expired(now, out PairingStep? expired))
            return expired;
        return IsEnded ? Ignored() : Fail(refusal);
    }

    /// <summary>The user confirms that both screens show the same string (<c>ConfirmPairing</c>).</summary>
    /// <param name="now">The current time.</param>
    /// <returns>
    /// From Confirm: Confirming, or Paired when the other side confirmed first, sending this side's confirmation (in
    /// <see cref="PairingMode.AddDevice"/> only once the device confirmed). <see cref="PairingResult.Invalid"/> while
    /// Waiting; Ok and unchanged while Confirming; <see cref="PairingResult.NotFound"/> once ended.
    /// </returns>
    public PairingStep OnConfirm(DateTimeOffset now)
    {
        if (Expired(now, out PairingStep? expired))
            return expired;

        return State switch
        {
            PairingState.Waiting => Step(PairingResult.Invalid, this),
            PairingState.Confirming => Step(PairingResult.Ok, this),
            PairingState.Confirm when Mode == PairingMode.AddDevice => PeerConfirmed
                ? Step(PairingResult.Ok, With(PairingState.Paired), PairingAction.SendEnrolment)
                : Step(PairingResult.Ok, With(PairingState.Confirming)),
            PairingState.Confirm => Step(PairingResult.Ok, With(PeerConfirmed ? PairingState.Paired : PairingState.Confirming), PairingAction.SendConfirm),
            _ => Step(PairingResult.NotFound, this),
        };
    }

    /// <summary>The user cancels (<c>CancelPairing</c>).</summary>
    /// <param name="now">The current time.</param>
    /// <returns>Cancelled, sending <c>PairCancel(Cancelled)</c>; <see cref="PairingResult.NotFound"/> once ended.</returns>
    public PairingStep OnCancel(DateTimeOffset now)
    {
        if (Expired(now, out PairingStep? expired))
            return expired;
        if (IsEnded)
            return Step(PairingResult.NotFound, this);

        var cancel = new PairCancel(PairCancelReason.Cancelled, "the user cancelled");
        return new PairingStep(PairingResult.Ok, With(PairingState.Cancelled), PairingAction.SendCancel, cancel);
    }

    /// <summary>
    /// The other side's <c>PairConfirm</c> arrived; in <see cref="PairingMode.ShowDevice"/> its Roster already passed
    /// <see cref="PairingRules.TryAcceptEnrolment"/>.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns>
    /// Confirming becomes Paired (in <see cref="PairingMode.AddDevice"/> sending the enrolment); in Confirm it is
    /// remembered, except that an enrolment before the device's own confirmation fails the pairing.
    /// </returns>
    public PairingStep OnPeerConfirm(DateTimeOffset now)
    {
        if (Expired(now, out PairingStep? expired))
            return expired;
        if (PeerConfirmed || State is not (PairingState.Confirm or PairingState.Confirming))
            return Ignored();

        if (State == PairingState.Confirm)
        {
            return Mode == PairingMode.ShowDevice
                ? Fail(new PairCancel(PairCancelReason.Violation, "the enrolment arrived before this device confirmed"))
                : Step(PairingResult.Ok, With(State, peerConfirmed: true));
        }

        return Mode == PairingMode.AddDevice
            ? Step(PairingResult.Ok, With(PairingState.Paired, peerConfirmed: true), PairingAction.SendEnrolment)
            : Step(PairingResult.Ok, With(PairingState.Paired, peerConfirmed: true));
    }

    /// <summary>The other side's <c>PairCancel</c> arrived.</summary>
    /// <param name="cancel">The message.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Cancelled when its user cancelled, Failed otherwise; nothing is sent back.</returns>
    public PairingStep OnPeerCancel(PairCancel cancel, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cancel);
        if (Expired(now, out PairingStep? expired))
            return expired;
        if (IsEnded)
            return Ignored();
        return cancel.Reason == PairCancelReason.Cancelled
            ? Step(PairingResult.Ok, With(PairingState.Cancelled))
            : Step(PairingResult.Ok, With(PairingState.Failed, failure: $"the other side ended the pairing: {cancel.Reason} {cancel.Detail}".TrimEnd()));
    }

    /// <summary>The pairing connection closed, or could not be opened.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>
    /// A showing side that still waits keeps waiting for another connection; any other unended pairing fails.
    /// </returns>
    public PairingStep OnDisconnected(DateTimeOffset now)
    {
        if (Expired(now, out PairingStep? expired))
            return expired;
        if (IsEnded)
            return Ignored();
        if (State == PairingState.Waiting && Mode is PairingMode.ShowUser or PairingMode.ShowDevice)
            return Step(PairingResult.Ok, this);
        return Step(PairingResult.Ok, With(PairingState.Failed, failure: "the connection closed before the pairing completed"));
    }

    /// <summary>Time passed: a pairing still running at <see cref="Expires"/> fails.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>Failed and sending <c>PairCancel(Expired)</c> once expired; unchanged otherwise.</returns>
    public PairingStep OnTick(DateTimeOffset now) => Expired(now, out PairingStep? expired) ? expired : Step(PairingResult.Ok, this);

    /// <inheritdoc />
    public override string ToString() => State == PairingState.Failed ? $"{Mode} Failed: {Failure}" : $"{Mode} {State}";

    private static TimeSpan Lifetime => TimeSpan.FromSeconds(Limits.PairingSeconds);

    private bool Expired(DateTimeOffset now, [NotNullWhen(true)] out PairingStep? step)
    {
        step = !IsEnded && now >= Expires
            ? new PairingStep(PairingResult.NotFound, With(PairingState.Failed, failure: "the pairing expired after 10 minutes"), PairingAction.SendCancel,
                new PairCancel(PairCancelReason.Expired, "the pairing expired after 10 minutes"))
            : null;
        return step is not null;
    }

    private PairingStep Fail(PairCancel cancel) =>
        new(PairingResult.Ok, With(PairingState.Failed, failure: cancel.Detail), PairingAction.SendCancel, cancel);

    private PairingStep Ignored() => Step(PairingResult.Invalid, this);

    private static PairingStep Step(PairingResult result, PairingMachine next) => new(result, next, PairingAction.None, null);

    private static PairingStep Step(PairingResult result, PairingMachine next, PairingAction action) => new(result, next, action, null);

    private PairingMachine With(PairingState state, string? sas = null, bool? peerConfirmed = null, string? failure = null) =>
        new(Mode, state, Expires, sas ?? Sas, peerConfirmed ?? PeerConfirmed, failure ?? string.Empty);
}
