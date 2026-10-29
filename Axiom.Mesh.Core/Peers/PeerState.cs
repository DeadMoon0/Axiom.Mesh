namespace Axiom.Mesh.Core;

/// <summary>Where a peer device stands with this node (SPEC.md §4.4, §6.2, §12.1).</summary>
public enum PeerState
{
    /// <summary>Heard of or met before, no session now and nothing holding a dial back: the resting state.</summary>
    Discovered = 0,

    /// <summary>A dial is under way: admission reserved a place and the connection is being opened.</summary>
    Dialling = 1,

    /// <summary>A TLS session is open and the peer's <c>Hello</c> has not arrived yet (SPEC.md §6.2).</summary>
    Connected = 2,

    /// <summary>A session is established: the peer's <c>Hello</c> arrived and passed its checks (SPEC.md §7.1).</summary>
    Established = 3,

    /// <summary>The peer closed before its <c>Hello</c> (a refusal of this node, SPEC.md §6.2); no dial until the backoff ends.</summary>
    Refused = 4,

    /// <summary>A dial failed, or the peer broke the protocol three times within an hour; no dial until the backoff ends (SPEC.md §4.4, §12.1).</summary>
    BackedOff = 5,
}
