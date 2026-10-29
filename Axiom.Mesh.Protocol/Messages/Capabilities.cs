using System;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// The capability flags a <c>Hello</c> carries (SPEC.md §14.3). Bits 2 to 31 are reserved: sent as 0, and dropped when
/// received, which is how an unknown flag is ignored (§1.4).
/// </summary>
[Flags]
public enum Capabilities : uint
{
    /// <summary>No flag set.</summary>
    None = 0,

    /// <summary>Bit 0: the sender follows new granted Packages automatically.</summary>
    FollowsNewGrants = 1 << 0,

    /// <summary>Bit 1: the sender is a full mirror (desktop retention) for every Package it lists.</summary>
    FullMirror = 1 << 1,
}
