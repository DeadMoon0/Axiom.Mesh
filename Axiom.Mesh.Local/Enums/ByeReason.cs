namespace Axiom.Mesh.Local;

/// <summary>Why a side sends <c>Bye</c> (LOCAL-SPEC.md §4.3, §14.3).</summary>
/// <remarks>A value this enumeration does not name is read as <see cref="Unspecified"/> (§4.3).</remarks>
public enum ByeReason : byte
{
    /// <summary>No reason given, or a reason this side does not know.</summary>
    Unspecified = 0,

    /// <summary>The Host stops, after <c>Drain</c> (§4.5).</summary>
    Shutdown = 1,

    /// <summary>A second connection for a live service name (§4.2).</summary>
    Duplicate = 2,

    /// <summary>The peer violated the protocol (§11.3).</summary>
    Violation = 3,

    /// <summary><c>Hello</c> not accepted; the detail is <c>version</c>, <c>service</c> or <c>busy</c> (§4.2).</summary>
    Refused = 4,

    /// <summary>The client did not answer <c>Ping</c>, or did not accept frames (§4.4, §7.4).</summary>
    Unresponsive = 5,

    /// <summary>A client that is finished (§4.3).</summary>
    Done = 6,
}
