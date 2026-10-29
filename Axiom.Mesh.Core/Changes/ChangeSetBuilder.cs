using System.Collections.Generic;

namespace Axiom.Mesh.Core;

/// <summary>Collects what a batch changes, for the <see cref="ChangeSet"/> published once it commits.</summary>
internal sealed class ChangeSetBuilder
{
    /// <summary>The objects stored.</summary>
    public List<StoredObject> Stored { get; } = [];

    /// <summary>The Entries stored.</summary>
    public List<StoredEntry> Entries { get; } = [];

    /// <summary>The Package changes.</summary>
    public List<PackageChange> Packages { get; } = [];

    /// <summary>The Entries newly redacted.</summary>
    public List<ObjectHash> Redacted { get; } = [];

    /// <summary>The pins added and removed.</summary>
    public List<PinChange> Pins { get; } = [];

    /// <summary>The users whose Roster or pairing changed.</summary>
    public List<UserId> Users { get; } = [];

    /// <summary>The objects evicted.</summary>
    public List<ObjectHash> Evicted { get; } = [];

    /// <summary>Whether trust changed.</summary>
    public bool TrustChanged { get; set; }

    /// <summary>Whether anything at all changed.</summary>
    public bool IsEmpty =>
        Stored.Count == 0 && Entries.Count == 0 && Packages.Count == 0 && Redacted.Count == 0 && Pins.Count == 0
        && Users.Count == 0 && Evicted.Count == 0 && !TrustChanged;

    /// <summary>Records a Package change, once per Package and kind.</summary>
    /// <param name="change">The change.</param>
    public void Package(PackageChange change)
    {
        if (!Packages.Contains(change))
            Packages.Add(change);
    }

    /// <summary>Freezes what was collected.</summary>
    /// <param name="totals">The totals after the batch.</param>
    /// <param name="trust">The trust after the batch.</param>
    /// <returns>The change set.</returns>
    public ChangeSet Build(StoreTotals totals, TrustSnapshot trust) => new()
    {
        Stored = Stored,
        Entries = Entries,
        Packages = Packages,
        Redacted = Redacted,
        Pins = Pins,
        Users = Users,
        Evicted = Evicted,
        Totals = totals,
        Trust = trust,
        TrustChanged = TrustChanged,
    };
}
