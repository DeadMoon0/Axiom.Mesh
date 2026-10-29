namespace Axiom.Mesh.Local;

/// <summary>What changed in a <c>PackageChanged</c> event (LOCAL-SPEC.md §14.12).</summary>
/// <remarks>Values 8 to 255 are reserved; a value this enumeration does not name is kept and tolerated (§1.5).</remarks>
public enum PackageChange : byte
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Created here, or started following.</summary>
    Followed = 1,

    /// <summary>A new current Manifest; for a Listed Package the first one.</summary>
    Manifest = 2,

    /// <summary>A new current Grant, in <c>Grant</c>.</summary>
    Grant = 3,

    /// <summary>A new or changed Head, in <c>Head</c>.</summary>
    Head = 4,

    /// <summary>A new local policy.</summary>
    Policy = 5,

    /// <summary>Stopped following.</summary>
    Unfollowed = 6,

    /// <summary>A peer listed a Package this Host did not know.</summary>
    Listed = 7,
}
