namespace Axiom.Mesh;

/// <summary>Which of two sessions to one device a node keeps (SPEC.md §4.5); the other is closed with <c>Bye(Duplicate)</c>.</summary>
public enum KeptSession
{
    /// <summary>Keep the session that became established last.</summary>
    New = 0,

    /// <summary>Keep the session that was established first.</summary>
    Old = 1,
}
