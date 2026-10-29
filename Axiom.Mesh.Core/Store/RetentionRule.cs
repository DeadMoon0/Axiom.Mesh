namespace Axiom.Mesh.Core;

/// <summary>
/// The retention table of SPEC.md §11.2 and the local policy of LOCAL-SPEC.md §14.14, as pure functions: which row
/// applies to a Package, and whether that row keeps (pins with reason <c>Policy</c>) an object an Entry, Tree or
/// ChunkList of the Package references. Entries, Manifests, Grants and Rosters are pinned with reason <c>Head</c> on
/// every device class and are not this rule's business.
/// </summary>
internal static class RetentionRule
{
    /// <summary>The row that applies: the local policy, else the Manifest's hint, else the device class's default.</summary>
    /// <param name="deviceClass">The node's device class.</param>
    /// <param name="policy">The Package's local policy.</param>
    /// <param name="hint">The owner's retention hint from the current Manifest.</param>
    /// <returns>The row.</returns>
    public static RetentionRow Effective(DeviceClass deviceClass, LocalRetention policy, Retention hint) => policy switch
    {
        LocalRetention.Everything => RetentionRow.Everything,
        LocalRetention.LatestOnly => RetentionRow.LatestOnly,
        _ => hint switch
        {
            Retention.Everything => RetentionRow.Everything,
            Retention.LatestOnly => RetentionRow.LatestOnly,
            _ => deviceClass == DeviceClass.Phone ? RetentionRow.LatestOnly : RetentionRow.Everything,
        },
    };

    /// <summary>
    /// Whether a row keeps an object that <paramref name="referrer"/> references in a Package of <paramref name="kind"/>.
    /// For an Archive under the latest-only row, the caller asks only for the newest Version's references.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="kind">The Package's kind.</param>
    /// <param name="referrer">The tag of the referencing object: an Entry, a Tree or a ChunkList.</param>
    /// <param name="target">The tag of the referenced object.</param>
    /// <returns><see langword="true"/> when the object is pinned with reason <c>Policy</c>.</returns>
    public static bool Keeps(RetentionRow row, PackageKind kind, ObjectTag referrer, ObjectTag target)
    {
        if (row == RetentionRow.Everything)
            return true;

        // The phone row: an Archive keeps its newest Version's Tree and ChunkLists, and fetches Chunks on demand; a
        // Channel keeps its Messages (as Entries) and fetches attachments on demand; other kinds keep their history.
        return kind == PackageKind.Archive && referrer switch
        {
            ObjectTag.Entry => target == ObjectTag.Tree,
            ObjectTag.Tree => target is ObjectTag.Tree or ObjectTag.ChunkList,
            _ => false,
        };
    }
}
