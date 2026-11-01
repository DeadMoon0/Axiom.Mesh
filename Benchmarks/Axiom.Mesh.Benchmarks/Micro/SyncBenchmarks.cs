using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Axiom.Mesh.Sync;
using BenchmarkDotNet.Attributes;

namespace Axiom.Mesh.Benchmarks.Micro;

/// <summary>
/// The sync service's rules on folders of their real size (plan §9.4 item 6): finding nothing changed in a scan of 100 000
/// files (target 100 ms, no allocation per file), merging two leaves of 10 000 entries both changed (target 50 ms), flattening
/// a Tree of 10 000 entries and building it again (target 50 ms).
/// </summary>
[MemoryDiagnoser]
public class SyncBenchmarks
{
    private const int Scanned = 100_000;
    private const int Entries = 10_000;
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly long Old = Now.AddHours(-1).UtcTicks;
    private ScannedFile[] scan = [];
    private Dictionary<string, Fingerprint> known = [];
    private Tree ancestor = null!;
    private Tree ours = null!;
    private Tree theirs = null!;
    private Dictionary<ObjectHash, Tree> trees = [];

    /// <summary>A scan of 100 000 unchanged files with their fingerprints, and three Trees of 10 000 entries: an ancestor and two leaves that each changed a tenth, some paths alike.</summary>
    [GlobalSetup]
    public void Setup()
    {
        scan = [.. Enumerable.Range(0, Scanned).Select(i => new ScannedFile($"dir-{i / 1_000:D3}/f{i:D6}", TreeEntryMode.File, (ulong)(i % 4_096), Old))];
        known = scan.ToDictionary(f => f.Path, f => new Fingerprint(f.Path, f.Mode, f.Size, f.WriteTicks, Hash(f.Path), Now.UtcTicks), StringComparer.Ordinal);
        ancestor = Build(i => $"content {i}", trees);
        ours = Build(i => i % 10 == 0 ? $"ours {i}" : $"content {i}", trees);
        theirs = Build(i => i % 10 == 0 ? (i % 20 == 0 ? $"ours {i}" : $"theirs {i}") : i % 17 == 0 ? $"theirs {i}" : $"content {i}", trees);
    }

    /// <summary>ChangeDetection over 100 000 unchanged files.</summary>
    /// <returns>How many were reused.</returns>
    [Benchmark]
    public int DetectChangesIn100kUnchanged() => ChangeDetection.Of(scan, known, Now, TimeSpan.FromSeconds(2), executableBitKnown: true).Reused.Count;

    /// <summary>The merge of two leaves of 10 000 entries, a tenth changed on each side, conflicts among them.</summary>
    /// <returns>The copies kept.</returns>
    [Benchmark]
    public int MergeOf10kEntriesBothSidesChanged() =>
        Merge.TryOf(ancestor, ours, theirs, new EntryKey(5, default, default), new EntryKey(6, default, default), trees, out MergedTree? merged, out _) ? merged!.Copies.Count : -1;

    /// <summary>A Tree of 10 000 entries in 10 directories flattened and built again.</summary>
    /// <returns>The rebuilt root's hash.</returns>
    [Benchmark]
    public ObjectHash FlattenAndRebuild10kEntries()
    {
        TreeFlattener.TryOf(ours, trees, out FlatTree flat, out _);
        return TreeSet.TryBuild(flat, out TreeSet? set, out _) ? set!.Root : default;
    }

    private static Tree Build(Func<int, string> text, Dictionary<ObjectHash, Tree> into)
    {
        FlatTree flat = FlatTree.Of(Enumerable.Range(0, Entries).Select(i => new KeyValuePair<string, FlatEntry>($"dir-{i / 1_000}/file-{i:D5}.txt", new FlatEntry(TreeEntryMode.File, Hash(text(i)), 10))));
        TreeSet.TryBuild(flat, out TreeSet? set, out _);
        foreach (Tree tree in set!.Trees)
            into[tree.Hash] = tree;
        return set.Trees[^1];
    }

    private static ObjectHash Hash(string text) => new(Hash32.Of(Encoding.UTF8.GetBytes(text)));
}
