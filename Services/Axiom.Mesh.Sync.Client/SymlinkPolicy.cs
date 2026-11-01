namespace Axiom.Mesh.Sync;

/// <summary>
/// What a binding does with a symlink of the branch (SPEC.md §8.4 mode 4, §11.4). Local symlinks are never read nor
/// published, whatever the policy.
/// </summary>
public enum SymlinkPolicy : byte
{
    /// <summary>Assigned to nothing; a request with it is invalid.</summary>
    Unassigned = 0,

    /// <summary>A symlink is written as a regular file holding its target text (the default of SPEC.md §11.4).</summary>
    AsFile = 1,

    /// <summary>A symlink is not written at all.</summary>
    Ignore = 2,
}
