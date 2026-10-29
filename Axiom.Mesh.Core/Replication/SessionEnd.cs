namespace Axiom.Mesh.Core;

/// <summary>How an established session's read loop ended, and for a violation what the peer did.</summary>
/// <param name="Kind">How it ended.</param>
/// <param name="Detail">For a violation, what the peer did, citing the section; else empty.</param>
internal readonly record struct SessionEnd(SessionEndKind Kind, string Detail)
{
    /// <summary>The peer broke a rule.</summary>
    /// <param name="detail">What it did.</param>
    /// <returns>The end.</returns>
    public static SessionEnd Violation(string detail) => new(SessionEndKind.Violation, detail);
}
