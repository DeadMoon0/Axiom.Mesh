using System.Net;
using Axiom.Mesh.Protocol;

namespace Axiom.Mesh.Core;

/// <summary>
/// Whether a pairing is under way (PAIR-SPEC.md §4), as the TLS layer must know it: the showing side offers the pairing
/// ALPN beside AMP/1 and accepts any P-256 client key under it; the entering side dials one address of its target,
/// offers only the pairing ALPN and pins the target's device (the code's key by its hash, or the id discovery
/// announced, §2.4). Immutable; the pairing shell swaps it through a <see cref="PairingGate"/>. <see cref="Closed"/> at
/// every other time.
/// </summary>
internal sealed class PairingWindow
{
    private PairingWindow(bool showing, PairingTarget? enteringTarget, IPEndPoint? enteringAddress)
    {
        IsShowing = showing;
        EnteringTarget = enteringTarget;
        EnteringAddress = enteringAddress;
    }

    /// <summary>No pairing: only AMP/1 is offered, and only roster keys are accepted.</summary>
    public static PairingWindow Closed { get; } = new(false, null, null);

    /// <summary>A pairing waits on this, the showing side: the mesh listener offers the pairing ALPN too (PAIR-SPEC.md §4.1).</summary>
    public static PairingWindow Showing { get; } = new(true, null, null);

    /// <summary>Whether the mesh listener offers the pairing ALPN and accepts an unknown P-256 key under it.</summary>
    public bool IsShowing { get; }

    /// <summary>The target being entered, whose device the entering dial pins; <see langword="null"/> when not entering.</summary>
    public PairingTarget? EnteringTarget { get; }

    /// <summary>The address of the target being dialled now; <see langword="null"/> when not entering.</summary>
    public IPEndPoint? EnteringAddress { get; }

    /// <summary>This side enters a target: the dial to <paramref name="address"/> is a pairing dial pinned to the target's device.</summary>
    /// <param name="target">What the entering side was given: a code, or a discovered device (PAIR-SPEC.md §2).</param>
    /// <param name="address">The address of the target being dialled.</param>
    /// <returns>The window.</returns>
    public static PairingWindow Entering(PairingTarget target, IPEndPoint address) => new(false, target, address);

    /// <summary>Whether a dial to <paramref name="remote"/> is the entering side's pairing dial.</summary>
    /// <param name="remote">The dialled address.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsEnteringDial(IPEndPoint remote) => EnteringTarget is not null && remote.Equals(EnteringAddress);
}
