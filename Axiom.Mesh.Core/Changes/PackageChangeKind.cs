namespace Axiom.Mesh.Core;

/// <summary>What changed about a Package (LOCAL-SPEC.md §14.12, whose values these are).</summary>
internal enum PackageChangeKind : byte
{
    /// <summary>Nothing; never published.</summary>
    None = 0,

    /// <summary>Created here, or started following.</summary>
    Followed = 1,

    /// <summary>A new current Manifest.</summary>
    Manifest = 2,

    /// <summary>A new current Grant.</summary>
    Grant = 3,

    /// <summary>A new or changed Head.</summary>
    Head = 4,

    /// <summary>A new local policy.</summary>
    Policy = 5,

    /// <summary>Stopped following.</summary>
    Unfollowed = 6,

    /// <summary>A peer listed a Package this node did not know.</summary>
    Listed = 7,
}
