namespace Axiom.Mesh.Sync;

/// <summary>
/// The place local changes not yet written take in the total order (SPEC.md §9.3): the Version this device would write next
/// gets a clock one above every clock the Archive holds, so it is later than every leaf stored now. A first sync merges a
/// non-empty directory as this virtual leaf, so local files win a conflict and the stored leaf's file is kept beside. Pure.
/// </summary>
internal static class VirtualLeaf
{
    /// <summary>The key of the Version this device would write next.</summary>
    /// <param name="latestClock">The highest clock of any Entry the Archive holds here.</param>
    /// <param name="own">This device.</param>
    /// <returns>The key; its hash is zero, which no written Entry has.</returns>
    public static EntryKey Key(ulong latestClock, DeviceId own) => new(latestClock + 1, own, default);
}
