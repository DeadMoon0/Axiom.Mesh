namespace Axiom.Mesh.Local;

/// <summary>How the last dial of a static peer ended, <c>StaticPeer.lastResult</c> (LOCAL-SPEC.md §5.9).</summary>
/// <remarks>A value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum DialResult : byte
{
    /// <summary>Never dialled.</summary>
    NeverDialled = 0,

    /// <summary>A session was established.</summary>
    Established = 1,

    /// <summary>The peer refused.</summary>
    Refused = 2,

    /// <summary>Nothing answered at the address.</summary>
    Unreachable = 3,
}
