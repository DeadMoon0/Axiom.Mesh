using System.Collections.Generic;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The result of merging two leaves (SPEC.md §9.5): the merged root Tree, the Trees the merge made that neither side had, the
/// copies kept beside conflicting paths, and whether the merge equals the later leaf's Tree (then no merge Version need be
/// written for two leaves). Made only by <see cref="Merge.TryOf"/>.
/// </summary>
internal sealed class MergedTree
{
    internal MergedTree(ObjectHash root, IReadOnlyList<Tree> made, IReadOnlyList<ConflictCopy> copies, bool equalsLater)
    {
        Root = root;
        Made = made;
        Copies = copies;
        EqualsLater = equalsLater;
    }

    /// <summary>The merged root Tree.</summary>
    public ObjectHash Root { get; }

    /// <summary>The Trees the merge made, the deepest first; every other Tree below <see cref="Root"/> is one of the sides'.</summary>
    public IReadOnlyList<Tree> Made { get; }

    /// <summary>The copies kept beside conflicting paths, in path order.</summary>
    public IReadOnlyList<ConflictCopy> Copies { get; }

    /// <summary>Whether <see cref="Root"/> is the Tree of the leaf later in the total order.</summary>
    public bool EqualsLater { get; }

    /// <inheritdoc />
    public override string ToString() => $"merged {Root}, {Copies.Count} copies{(EqualsLater ? ", equals the later leaf" : string.Empty)}";
}
