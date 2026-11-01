using System;
using System.Collections.Generic;
using System.Linq;
using static Axiom.Mesh.Sync.Tests.Support.Facts;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// SYNC-SERVICE.md §3 step 3: a file is read again only when its size or write time differ from its fingerprint; a changed file
/// is read once it has been quiet for the stability time; a fingerprint the scan did not find is a deletion.
/// </summary>
public sealed class ChangeDetectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly long LongAgo = Now.AddMinutes(-5).UtcTicks;

    [Fact]
    public void AFileWhoseSizeAndWriteTimeMatchItsFingerprintIsNotReadAgain()
    {
        var print = new Fingerprint("a.txt", TreeEntryMode.File, 5, LongAgo, Hash(1), Now.UtcTicks);

        ChangeSet changes = ChangeDetection.Of([new ScannedFile("a.txt", TreeEntryMode.File, 5, LongAgo)], Known(print), Now, TimeSpan.FromSeconds(2), executableBitKnown: true);

        Assert.Equal([print], changes.Reused);
        Assert.True(changes.Unchanged);
    }

    [Fact]
    public void ANewOrChangedFileIsReadOnceQuiet_BusyBefore()
    {
        var print = new Fingerprint("a.txt", TreeEntryMode.File, 5, LongAgo, Hash(1), Now.UtcTicks);
        ScannedFile grown = new("a.txt", TreeEntryMode.File, 6, LongAgo + 1);
        ScannedFile fresh = new("b.txt", TreeEntryMode.File, 1, Now.AddMilliseconds(-500).UtcTicks);
        ScannedFile settled = new("c.txt", TreeEntryMode.File, 1, Now.AddSeconds(-3).UtcTicks);

        ChangeSet changes = ChangeDetection.Of([grown, fresh, settled], Known(print), Now, TimeSpan.FromSeconds(2), executableBitKnown: true);

        Assert.Equal([grown, settled], changes.ToHash);
        Assert.Equal([fresh], changes.Busy);
        Assert.Empty(changes.Reused);
    }

    [Fact]
    public void AFingerprintTheScanDidNotFindIsADeletion()
    {
        var kept = new Fingerprint("kept.txt", TreeEntryMode.File, 1, LongAgo, Hash(1), Now.UtcTicks);
        var gone = new Fingerprint("gone.txt", TreeEntryMode.File, 1, LongAgo, Hash(2), Now.UtcTicks);

        ChangeSet changes = ChangeDetection.Of([new ScannedFile("kept.txt", TreeEntryMode.File, 1, LongAgo)], Known(kept, gone), Now, TimeSpan.FromSeconds(2), executableBitKnown: false);

        Assert.Equal(["gone.txt"], changes.Deleted);
        Assert.Equal([kept], changes.Reused);
    }

    // A fingerprint taken within two seconds of its file's last write may have missed a write in the same tick with the same
    // size: the file is read again until a fingerprint is taken later; one the service wrote itself is trusted.
    [Fact]
    public void AFingerprintTakenRightAfterTheWriteIsRacy_TheFileIsReadAgain()
    {
        long written = Now.AddSeconds(-30).UtcTicks;
        var racy = new Fingerprint("racy.txt", TreeEntryMode.File, 6, written, Hash(1), written + TimeSpan.FromMilliseconds(5).Ticks);
        var trusted = new Fingerprint("trusted.txt", TreeEntryMode.File, 6, written, Hash(2), written + TimeSpan.FromSeconds(3).Ticks);
        var ownWrite = new Fingerprint("pulled.txt", TreeEntryMode.File, 6, written, Hash(3), long.MaxValue);
        ScannedFile[] scan = [new("racy.txt", TreeEntryMode.File, 6, written), new("trusted.txt", TreeEntryMode.File, 6, written), new("pulled.txt", TreeEntryMode.File, 6, written)];

        ChangeSet changes = ChangeDetection.Of(scan, Known(racy, trusted, ownWrite), Now, TimeSpan.Zero, executableBitKnown: true);

        Assert.Equal(["racy.txt"], changes.ToHash.Select(f => f.Path));
        Assert.Equal([trusted, ownWrite], changes.Reused);
    }

    private static Dictionary<string, Fingerprint> Known(params Fingerprint[] prints)
    {
        var known = new Dictionary<string, Fingerprint>(StringComparer.Ordinal);
        foreach (Fingerprint print in prints)
            known[print.Path] = print;
        return known;
    }
}
