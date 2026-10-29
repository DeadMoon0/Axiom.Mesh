namespace Axiom.Mesh.Core;

/// <summary>What a <see cref="PackageCommand"/> does.</summary>
internal enum PackageCommandKind
{
    /// <summary>A peer lists the Package: a Package not known here becomes Listed (SPEC.md §1.2).</summary>
    List = 0,

    /// <summary>Follow it: its Manifest, Grants and Entries are pinned, and its policy applied.</summary>
    Follow = 1,

    /// <summary>Stop following it: back to Listed, its Head and Policy pins released.</summary>
    Unfollow = 2,

    /// <summary>Set its local retention policy (LOCAL-SPEC.md §14.14).</summary>
    SetPolicy = 3,
}
