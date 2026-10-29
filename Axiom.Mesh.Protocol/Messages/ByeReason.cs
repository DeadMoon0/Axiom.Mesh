namespace Axiom.Mesh.Protocol;

/// <summary>Why a session closes (SPEC.md §14.2). A value this enumeration does not name is read as <see cref="Unspecified"/>.</summary>
public enum ByeReason : byte
{
    /// <summary>No reason given, or one this node does not know.</summary>
    Unspecified = 0,

    /// <summary>The node shuts down.</summary>
    Shutdown = 1,

    /// <summary>The duplicate rule closed this session (§4.5).</summary>
    Duplicate = 2,

    /// <summary>The peer broke the protocol (§12.1).</summary>
    Violation = 3,

    /// <summary>The peer's <c>Hello</c> failed its checks, or its device was removed (§7.1, §8.8).</summary>
    Unauthorized = 4,

    /// <summary>Nothing was received for 5 minutes (§6.3).</summary>
    Idle = 5,
}
