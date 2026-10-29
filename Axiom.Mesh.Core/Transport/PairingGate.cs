using System.Threading;

namespace Axiom.Mesh.Core;

/// <summary>
/// Holds the <see cref="PairingWindow"/> that applies now: the pairing shell (PAIR-SPEC.md) swaps it, every handshake
/// reads it without a lock. Starts <see cref="PairingWindow.Closed"/>.
/// </summary>
internal sealed class PairingGate
{
    private PairingWindow current = PairingWindow.Closed;

    /// <summary>The window that applies now.</summary>
    public PairingWindow Current => Volatile.Read(ref current);

    /// <summary>Swaps the window in; handshakes that already started keep the one they read.</summary>
    /// <param name="window">The new window.</param>
    public void Set(PairingWindow window) => Volatile.Write(ref current, window);
}
