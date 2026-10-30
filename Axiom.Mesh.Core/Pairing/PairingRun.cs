using System;
using System.Net;
using System.Threading;
using Axiom.Mesh.Local;
using Axiom.Mesh.Protocol;
using LocalMode = Axiom.Mesh.Local.PairingMode;
using LocalState = Axiom.Mesh.Local.PairingState;

namespace Axiom.Mesh.Core;

/// <summary>
/// One pairing as the shell runs it (PAIR-SPEC.md §6): its machine, the code it shows or entered, the other side once its
/// <c>PairHello</c> passed, and the connection that carries it. Changed only by the <see cref="PairingShell"/>, one event
/// at a time.
/// </summary>
internal sealed class PairingRun : IDisposable
{
    private readonly CancellationTokenSource stop = new();

    /// <summary>A pairing that starts.</summary>
    /// <param name="id">Its id, never 0.</param>
    /// <param name="machine">Its machine, Waiting.</param>
    /// <param name="shown">The code this side shows; <see langword="null"/> on the entering side.</param>
    /// <param name="entered">The code this side entered; <see langword="null"/> on the showing side.</param>
    public PairingRun(uint id, PairingMachine machine, PairingCode? shown, PairingCode? entered)
    {
        Id = id;
        Machine = machine;
        Shown = shown;
        Entered = entered;
    }

    /// <summary>The pairing's id (LOCAL-SPEC.md §5.15).</summary>
    public uint Id { get; }

    /// <summary>The machine as it stands.</summary>
    public PairingMachine Machine { get; set; }

    /// <summary>The code this side shows.</summary>
    public PairingCode? Shown { get; }

    /// <summary>The code this side entered.</summary>
    public PairingCode? Entered { get; }

    /// <summary>The other side, once its <c>PairHello</c> passed the checks of PAIR-SPEC.md §7.1.</summary>
    public PairPeer? Peer { get; set; }

    /// <summary>The connection that carries the pairing now; <see langword="null"/> while none does.</summary>
    public PairingLink? Link { get; set; }

    /// <summary>Whether a pairing connection was ever attached to this pairing.</summary>
    public bool EverLinked { get; set; }

    /// <summary>On the entering side, the address of the code that is dialled now or answered.</summary>
    public IPEndPoint? Dialled { get; set; }

    /// <summary>The timer that fails the pairing at its expiry.</summary>
    public ITimer? Expiry { get; set; }

    /// <summary>Cancelled when the pairing ends: the entering side stops dialling.</summary>
    public CancellationToken Stopping => stop.Token;

    /// <summary>Whether this side enters the code.</summary>
    public bool IsEntering => Entered is not null;

    /// <summary>The pairing as LOCAL-SPEC.md §5.15 shows it.</summary>
    /// <returns>The record.</returns>
    public Pairing Record() => new()
    {
        PairingId = Id,
        Mode = (LocalMode)(byte)Machine.Mode,
        State = (LocalState)(byte)Machine.State,
        Code = Shown?.Text ?? string.Empty,
        Sas = Machine.Sas,
        RemoteUserId = Peer?.UserKey?.AsUserId() ?? default,
        RemoteName = Peer?.Name ?? string.Empty,
        RemoteDeviceId = Peer?.Device ?? Entered?.DeviceKey.AsDeviceId() ?? default,
        Expires = LocalRecords.Milliseconds(Machine.Expires),
        Failure = Machine.Failure,
    };

    /// <summary>Stops what the pairing still runs: the entering side's dials, the expiry timer, the connection.</summary>
    public void Halt()
    {
        Expiry?.Dispose();
        Link?.End();
        try
        {
            stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Disposed meanwhile.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Halt();
        stop.Dispose();
    }

    /// <inheritdoc />
    public override string ToString() => $"pairing {Id}: {Machine}";
}
