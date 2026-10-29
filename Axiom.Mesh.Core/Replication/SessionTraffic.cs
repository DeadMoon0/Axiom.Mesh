using System;

namespace Axiom.Mesh.Core;

/// <summary>
/// What one established session to a peer has carried and holds queued (LOCAL-SPEC.md §5.8): bytes and objects each way,
/// wants outstanding each way, and what waits in its two lanes. A snapshot; read again for news.
/// </summary>
public sealed class SessionTraffic
{
    /// <summary>The peer's device.</summary>
    public required DeviceId Device { get; init; }

    /// <summary>The peer's user.</summary>
    public required UserId User { get; init; }

    /// <summary>The connection's number in the node's server.</summary>
    public required long Connection { get; init; }

    /// <summary>When the session was established, on the node's clock.</summary>
    public required DateTimeOffset Since { get; init; }

    /// <summary>Application bytes received on the connection.</summary>
    public required long BytesIn { get; init; }

    /// <summary>Application bytes sent on the connection.</summary>
    public required long BytesOut { get; init; }

    /// <summary>Objects received from the peer.</summary>
    public required long ObjectsIn { get; init; }

    /// <summary>Objects sent to the peer.</summary>
    public required long ObjectsOut { get; init; }

    /// <summary>Hashes this node has outstanding toward the peer (SPEC.md §7.5).</summary>
    public required int WantsOut { get; init; }

    /// <summary>Hashes the peer has outstanding toward this node, deferred ones included.</summary>
    public required int WantsIn { get; init; }

    /// <summary>Object bytes queued toward the peer, at most 8 MiB (SPEC.md §7.6).</summary>
    public required long QueuedObjectBytes { get; init; }

    /// <summary>Answers to <c>Want</c> deferred until the object queue drains.</summary>
    public required int DeferredWants { get; init; }

    /// <summary>Messages this node handed to the session for sending, from establishment on, its own heartbeats aside.</summary>
    public required long MessagesSent { get; init; }

    /// <summary>Messages the peer sent that this node has fully handled, from establishment on.</summary>
    public required long MessagesHandled { get; init; }

    /// <summary>Whether the session has nothing left to send, to handle or to push.</summary>
    public required bool IsIdle { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Device.ToString()[..16]} in {ObjectsIn} out {ObjectsOut} wants {WantsOut}/{WantsIn} queued {QueuedObjectBytes} deferred {DeferredWants} sent {MessagesSent} handled {MessagesHandled}{(IsIdle ? " idle" : string.Empty)}";
}
