using System;
using static Axiom.Mesh.Sync.Tests.Support.Facts;

namespace Axiom.Mesh.Sync.Tests.Regression;

/// <summary>
/// The keys of the service's per-file tables lead with the part that differs from row to row: LDB keys a byte column by its
/// first 8 bytes, so keys that all start with the binding's id would share one index key, and every insert would check every
/// row of the binding (a first sync of 10 000 files spent minutes in its fingerprint and put tables).
/// </summary>
public sealed class StoreKeyTests
{
    // Damage: a key led by the binding's id makes each insert into SyncFingerprints and SyncPuts linear in the binding's rows.
    [Fact]
    public void TwoFilesOrObjectsOfOneBindingDifferInTheFirstEightBytesOfTheirKeys()
    {
        byte[] binding = BindingIdentity.Of("/home/alice/shared", new PackageId(Id(0xa7)));

        Assert.False(RowBytes.PathKey(binding, "a.txt").AsSpan(0, 8).SequenceEqual(RowBytes.PathKey(binding, "b.txt").AsSpan(0, 8)));
        Assert.False(RowBytes.Keyed(binding, RowBytes.Of(Id(1))).AsSpan(0, 8).SequenceEqual(RowBytes.Keyed(binding, RowBytes.Of(Id(2))).AsSpan(0, 8)));
    }
}
