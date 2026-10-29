namespace Axiom.Mesh.Core;

/// <summary>Where a Package stands on this node (LOCAL-SPEC.md §5.4, SPEC.md §1.2); the values are those of LOCAL-SPEC.md.</summary>
internal enum PackageState : byte
{
    /// <summary>Not known here.</summary>
    None = 0,

    /// <summary>A peer lists it and the node does not follow it: at most its Manifest is held, unpinned (SPEC.md §10.6).</summary>
    Listed = 1,

    /// <summary>Replicated: its Manifest, Grants and Entries are stored and pinned.</summary>
    Followed = 2,
}
