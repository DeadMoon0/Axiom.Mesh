namespace Axiom.Mesh.Core;

/// <summary>What happened to a connection or a peer, as a <see cref="NodeFact"/> records it.</summary>
public enum NodeFactKind
{
    /// <summary>Nothing: the value of an uninitialised field, never recorded.</summary>
    None = 0,

    /// <summary>Admission let a connection in, accepted on the mesh port or dialled and adopted.</summary>
    Accepted = 1,

    /// <summary>Admission refused a connection or a dial before anything was read (SPEC.md §4.6).</summary>
    Rejected = 2,

    /// <summary>The TLS handshake failed or its peer was not acceptable: closed without a protocol byte (SPEC.md §4.3).</summary>
    HandshakeFailed = 3,

    /// <summary>A TLS session to a peer whose key passed opened; its <c>Hello</c> is awaited.</summary>
    SessionOpened = 4,

    /// <summary>The peer's <c>Hello</c> passed its checks: the session is established (SPEC.md §7.1).</summary>
    Established = 5,

    /// <summary>The peer's <c>Hello</c> failed its checks; closed with <c>Bye(Unauthorized)</c> (SPEC.md §7.1).</summary>
    HelloRefused = 6,

    /// <summary>The connection closed before the peer's <c>Hello</c>: a refusal of this node by the peer (SPEC.md §6.2).</summary>
    RefusedBeforeHello = 7,

    /// <summary>The peer broke the protocol; closed with <c>Bye(Violation)</c> and counted against it (SPEC.md §12.1).</summary>
    Violation = 8,

    /// <summary>The duplicate rule closed one of two sessions to a device with <c>Bye(Duplicate)</c> (SPEC.md §4.5).</summary>
    DuplicateClosed = 9,

    /// <summary>An established session was closed because its device left the node's trust (SPEC.md §8.8).</summary>
    Unauthorized = 10,

    /// <summary>A dial began: admission reserved a place for the address.</summary>
    Dialling = 11,

    /// <summary>A dial could not connect (SPEC.md §4.4).</summary>
    DialFailed = 12,

    /// <summary>Dials to a peer are held back until a time (SPEC.md §4.4, §6.2, §12.1).</summary>
    BackedOff = 13,

    /// <summary>A session ended.</summary>
    SessionEnded = 14,

    /// <summary>A connection closed.</summary>
    Closed = 15,

    /// <summary>A handler failed with an exception: a defect of the node, logged as an error.</summary>
    HandlerFaulted = 16,

    /// <summary>A listing from the peer completed: this node's view of what the peer holds was replaced (SPEC.md §7.2).</summary>
    ListingDone = 17,

    /// <summary>An object was stored, from a peer or written here; <see cref="NodeFact.Subject"/> is its hash (SPEC.md §11.1).</summary>
    ObjectStored = 18,

    /// <summary>This node answered one hash of a peer's <c>Want</c>, with <c>Object</c> or <c>NotFound</c> (SPEC.md §7.5); <see cref="NodeFact.Subject"/> is the hash.</summary>
    WantAnswered = 19,

    /// <summary>
    /// A peer sent faulty content (SPEC.md §12.2): malformed, an invalid signature, an Entry whose parents fail §8.6. It was
    /// dropped, never forwarded, and the device recorded as faulty; the session goes on. <see cref="NodeFact.Subject"/> is its hash.
    /// </summary>
    FaultyContent = 20,
}
