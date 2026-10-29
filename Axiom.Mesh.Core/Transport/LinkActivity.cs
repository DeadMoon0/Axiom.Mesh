using System;
using System.Threading;

namespace Axiom.Mesh.Core;

/// <summary>
/// When a connection last received a byte, on the node's clock: what the duplicate rule asks of an old session
/// ("received nothing for 60 seconds", SPEC.md §4.5), measured below the protocol so that a <c>Ping</c> or <c>Pong</c>
/// the definition answers by itself counts too. Set as a connection feature by <see cref="MeshTlsLayer"/>.
/// </summary>
internal sealed class LinkActivity
{
    private readonly TimeProvider time;
    private long lastReceived;

    /// <summary>Starts counting at the end of the handshake, which received bytes.</summary>
    /// <param name="time">The node's clock.</param>
    public LinkActivity(TimeProvider time)
    {
        this.time = time;
        lastReceived = time.GetTimestamp();
    }

    /// <summary>Records that bytes arrived now. Called on every read that returned data; one store, no allocation.</summary>
    public void Received() => Volatile.Write(ref lastReceived, time.GetTimestamp());

    /// <summary>How long the connection has received nothing.</summary>
    /// <returns>The time since the last byte arrived.</returns>
    public TimeSpan SilentFor() => time.GetElapsedTime(Volatile.Read(ref lastReceived));
}
