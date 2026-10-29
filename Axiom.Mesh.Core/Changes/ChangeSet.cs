using System.Collections.Generic;

namespace Axiom.Mesh.Core;

/// <summary>
/// What one committed batch of the store writer changed: objects and Entries stored (with their store sequences),
/// Package changes, Entries newly redacted, pins, users, evictions, the store's totals and the trust that holds from
/// now. Published on the <see cref="ChangeBus"/> after the commit, so a subscriber never sees a change that might
/// still roll back.
/// </summary>
internal sealed class ChangeSet
{
    /// <summary>The objects stored, in the order stored.</summary>
    public required IReadOnlyList<StoredObject> Stored { get; init; }

    /// <summary>The Entries stored, ascending by store sequence.</summary>
    public required IReadOnlyList<StoredEntry> Entries { get; init; }

    /// <summary>The Package changes, in the order they happened.</summary>
    public required IReadOnlyList<PackageChange> Packages { get; init; }

    /// <summary>Stored Entries a Redaction stored now withdraws (SPEC.md §9.4).</summary>
    public required IReadOnlyList<ObjectHash> Redacted { get; init; }

    /// <summary>Pins added and removed.</summary>
    public required IReadOnlyList<PinChange> Pins { get; init; }

    /// <summary>Users whose Roster or pairing changed.</summary>
    public required IReadOnlyList<UserId> Users { get; init; }

    /// <summary>Objects evicted; no node may claim them from now on (SPEC.md §11.3).</summary>
    public required IReadOnlyList<ObjectHash> Evicted { get; init; }

    /// <summary>The store's totals after the batch.</summary>
    public required StoreTotals Totals { get; init; }

    /// <summary>The trust that holds after the batch.</summary>
    public required TrustSnapshot Trust { get; init; }

    /// <summary>Whether the batch changed trust: a Roster, Manifest, Grant, follow, pairing or identity.</summary>
    public required bool TrustChanged { get; init; }
}
