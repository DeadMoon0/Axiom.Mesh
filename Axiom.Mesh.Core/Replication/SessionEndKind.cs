namespace Axiom.Mesh.Core;

/// <summary>How an established session's read loop ended.</summary>
internal enum SessionEndKind
{
    /// <summary>It did not end.</summary>
    None = 0,

    /// <summary>The peer said <c>Bye</c>.</summary>
    PeerBye = 1,

    /// <summary>The connection ended without a <c>Bye</c>.</summary>
    PeerClosed = 2,

    /// <summary>The node drains, or the session was asked to close (the duplicate rule, a removal).</summary>
    Cancelled = 3,

    /// <summary>The peer broke a rule the handler checks (SPEC.md §7.2, §7.5, §5.1): close with <c>Bye(Violation)</c>.</summary>
    Violation = 4,
}
