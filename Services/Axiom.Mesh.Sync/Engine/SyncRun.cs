using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Sync;

/// <summary>
/// One run of one binding (SYNC-SERVICE.md §3): scan the directory, read what changed into the store, build its Trees, plan
/// against the branch's leaves, publish, merge, write the target into the directory and commit the new base. Crash-safe by
/// these invariants, proved by <c>CrashResumeTests</c>:
/// <list type="number">
/// <item>everything put is content-addressed, so doing a step again changes nothing;</item>
/// <item>a Version is never written when a leaf of this device already names its Tree: a crash between writing it and recording
/// it makes no second one;</item>
/// <item>the target is recorded as pending before the directory changes and the base after it, so a run that resumes a
/// pending target writes only what is not yet in place;</item>
/// <item>a file changed in the directory since the base is never overwritten or deleted by a pull;</item>
/// <item>a read-only binding never writes an Entry.</item>
/// </list>
/// The disk, the Host and the clock come in through the shell; every decision is one of the rules.
/// </summary>
internal sealed class SyncRun
{
    private readonly ServiceSession session;
    private readonly SyncStore store;
    private readonly VersionIndex versions;
    private readonly BindingSpec spec;
    private readonly BindingRow row;
    private readonly SyncServiceOptions options;
    private readonly Action<BindingState> report;
    private readonly Dictionary<ObjectHash, Tree> trees = [];
    private readonly List<ConflictRow> conflicts = [];
    private readonly bool windows = OperatingSystem.IsWindows();
    private ObjectReader? reader;
    private bool readOnly;
    private ulong latestClock;

    /// <summary>A run.</summary>
    /// <param name="session">The connection to the Host.</param>
    /// <param name="store">The database.</param>
    /// <param name="versions">The Version DAG.</param>
    /// <param name="spec">The binding.</param>
    /// <param name="row">The binding's row; the run moves its base and pending Tree and saves it.</param>
    /// <param name="options">The clock, the stability time, the seams.</param>
    /// <param name="report">Told every state the run passes through.</param>
    public SyncRun(ServiceSession session, SyncStore store, VersionIndex versions, BindingSpec spec, BindingRow row, SyncServiceOptions options, Action<BindingState> report)
    {
        this.session = session;
        this.store = store;
        this.versions = versions;
        this.spec = spec;
        this.row = row;
        this.options = options;
        this.report = report;
    }

    private LocalClient Client => session.Client;

    private DeviceId Own => session.Host.Identity.DeviceId;

    /// <summary>Runs.</summary>
    /// <param name="cancellationToken">Cancelled when the binding ends, the service stops or the connection is lost.</param>
    /// <returns>How it ended.</returns>
    /// <exception cref="IOException">The file system or the Host refused a step; the binding is in error until the next run.</exception>
    public async Task<RunOutcome> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await StepsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Step 14: release every pin a run took, this one's and those of runs that stopped before their end.
            await ReleasePutsAsync().ConfigureAwait(false);
        }
    }

    private async Task<RunOutcome> StepsAsync(CancellationToken cancellationToken)
    {
        LocalResult<Package> package = await Client.SendAsync(new GetPackage { PackageId = spec.Package }, cancellationToken).ConfigureAwait(false);
        if (!package.IsOk || package.Value.State != PackageState.Followed)
            return RunOutcome.Failed(BindingState.Error, "the Archive is not followed on this Host; follow it again, or remove the binding");
        if (package.Value.Kind != PackageKind.Archive)
            return RunOutcome.Failed(BindingState.Error, "the Package is not an Archive");
        readOnly = package.Value.Rights < GrantRights.Write;
        latestClock = package.Value.LatestClock;
        if (!await CaughtUpAsync(cancellationToken).ConfigureAwait(false))
            return RunOutcome.Behind((BindingState)row.State);
        reader = await ObjectReader.OpenAsync(Client, cancellationToken).ConfigureAwait(false);

        // Steps 2 to 5: the directory as it is now.
        report(BindingState.Scanning);
        LocalState local = await ScanAsync(cancellationToken).ConfigureAwait(false);
        if (local.Problem is { } problem)
            return RunOutcome.Failed(local.ProblemState, problem);

        // Step 1: a run that stopped after recording its target resumes it.
        if (row.PendingTree is { } pending)
        {
            var target = new ObjectHash(RowBytes.Hash(pending));
            IReadOnlyList<ObjectHash> leaves = RowBytes.Split(row.PendingLeaves);
            if (!await TreeLoader.LoadAsync(reader, Client, spec.Package, target, trees, cancellationToken).ConfigureAwait(false))
                return Waiting("the Tree being written is not stored here");
            return await WriteTargetAsync(local, local.Base, target, leaves, "resumed", cancellationToken).ConfigureAwait(false);
        }

        // Steps 6 and 7: the branch, and the plan.
        IReadOnlyDictionary<ObjectHash, VersionFact> dag = versions.Of(spec.Package);
        IReadOnlyList<VersionFact> branch = Leaves.Of(dag, spec.Label);
        ObjectHash? baseTree = row.BaseTree is { } bytes ? new ObjectHash(RowBytes.Hash(bytes)) : null;
        SyncPlan plan = SyncPlanner.Plan(new PlanInput(readOnly, baseTree, RowBytes.Split(row.BaseLeaves), local.Root, TreeSet.EmptyTree.Hash, branch, dag));
        switch (plan.Kind)
        {
            case PlanKind.Nothing:
                uint changed = plan.LocalChanges ? Changes(local.Base, local.Flat) : 0;
                return await EndedAsync(plan.LocalChanges ? BindingState.LocalChangesReadOnly : BindingState.InSync, plan.ToString(), changed, local.Flat, local.HasBusy).ConfigureAwait(false);
            case PlanKind.Publish:
                await PublishAsync(local, plan.Parents, cancellationToken).ConfigureAwait(false);
                return await EndedAsync(BindingState.InSync, plan.ToString(), 0, local.Flat, local.HasBusy).ConfigureAwait(false);
            case PlanKind.Pull:
                if (!await FetchAsync([plan.Target!], cancellationToken).ConfigureAwait(false))
                    return Waiting("the Version's files are not stored here, and no peer supplied them yet");
                return await WriteTargetAsync(local, local.Base, plan.Target!.Tree, [plan.Target.Hash], plan.ToString(), cancellationToken).ConfigureAwait(false);
            case PlanKind.PublishThenMerge:
                FlatTree published = await PublishAsync(local, plan.Parents, cancellationToken).ConfigureAwait(false);
                return await MergeAsync(local, published, Leaves.Of(versions.Of(spec.Package), spec.Label), plan, cancellationToken).ConfigureAwait(false);
            case PlanKind.MergeChain or PlanKind.PullReadOnly:
                return await MergeAsync(local, local.Base, plan.Leaves, plan, cancellationToken).ConfigureAwait(false);
            case PlanKind.FirstSync:
                return await FirstSyncAsync(local, plan, cancellationToken).ConfigureAwait(false);
            default:
                throw new InvalidOperationException($"The planner chose {plan.Kind}, which a run does not know.");
        }
    }

    // Whether the Version DAG holds every Version the Host stored: planning on a DAG behind the Host could publish over a leaf.
    private async Task<bool> CaughtUpAsync(CancellationToken cancellationToken)
    {
        LocalResult<EntryPage> beyond = await Client.SendAsync(new ListStored { Kinds = [EntryKind.Version], FromSeq = versions.LastSeq + 1, Limit = 64 }, cancellationToken).ConfigureAwait(false);
        return beyond.IsOk && !beyond.Value.More && beyond.Value.Entries.All(e => !VersionFact.TryOf(e, out _) || versions.Contains(e.Hash));
    }

    // Steps 2 to 5: scan, detect, read what changed, build the Trees of the directory as it is now.
    private async Task<LocalState> ScanAsync(CancellationToken cancellationToken)
    {
        DirectoryScan scan = options.Scanner.Scan(spec.Directory, spec.Ignore, cancellationToken);
        if (!scan.Exists)
            return LocalState.Failed(BindingState.Error, $"the directory {spec.Directory} is gone; create it again, or remove the binding");

        FlatTree baseFlat = FlatTree.Empty;
        if (row.BaseTree is { } baseBytes)
        {
            var baseRoot = new ObjectHash(RowBytes.Hash(baseBytes));
            if (!await TreeLoader.LoadAsync(reader!, Client, spec.Package, baseRoot, trees, cancellationToken).ConfigureAwait(false) || !TreeFlattener.TryOf(trees[baseRoot], trees, out baseFlat, out _))
                return LocalState.Failed(BindingState.WaitingForObjects, "the Tree the directory last equalled is not stored here");
        }

        Dictionary<string, Fingerprint> known = await store.FingerprintsAsync(spec.Id, cancellationToken).ConfigureAwait(false);
        DateTimeOffset scanned = DateTimeOffset.UtcNow;
        ChangeSet changes = ChangeDetection.Of(scan.Files, known, scanned, options.Stability, executableBitKnown: !windows);
        HashedFiles hashed = await FileHasher.HashAsync(Client, spec.Directory, changes.ToHash, scanned.UtcTicks, cancellationToken).ConfigureAwait(false);
        await store.AddPutsAsync(spec.Id, hashed.Puts, cancellationToken).ConfigureAwait(false);
        await store.WriteFingerprintsAsync(spec.Id, hashed.Prints, [.. changes.Deleted, .. hashed.Vanished], Durability.Lazy, cancellationToken).ConfigureAwait(false);
        foreach (ScanProblem found in scan.Problems.Concat(hashed.Problems))
            conflicts.Add(Conflict(found.Path, found.Kind, string.Empty, default, default));

        var busy = new HashSet<string>(changes.Busy.Select(b => b.Path).Concat(hashed.Busy), StringComparer.Ordinal);
        string[] problemPaths = [.. scan.Problems.Concat(hashed.Problems).Select(p => p.Path)];
        IEnumerable<Fingerprint> stands = changes.Reused.Concat(hashed.Prints).Concat(busy.Where(known.ContainsKey).Select(p => known[p]));
        bool Carried(string path) =>
            spec.Ignore.Matches(path) || busy.Contains(path) || problemPaths.Any(p => p.Length == 0 || path == p || path.StartsWith(p + "/", StringComparison.Ordinal))
            || (spec.Symlinks == SymlinkPolicy.Ignore && baseFlat.At(path) is { Mode: TreeEntryMode.Symlink });
        FlatTree flat = LocalTree.Of(stands, scan.Directories, baseFlat, Carried);
        if (!TreeSet.TryBuild(flat, out TreeSet? set, out string? problem))
            return LocalState.Failed(BindingState.Error, $"the directory makes no Tree: {problem}");
        foreach (Tree tree in set!.Trees)
            trees[tree.Hash] = tree;
        return new LocalState(flat, baseFlat, set, busy, busy.Count > 0, null, BindingState.Unassigned);
    }

    // Step 8: publish the directory as a Version naming the given parents; the base is then the directory.
    private async Task<FlatTree> PublishAsync(LocalState local, IReadOnlyList<ObjectHash> parents, CancellationToken cancellationToken)
    {
        report(BindingState.Pushing);
        await PutAsync(local.Trees.Trees, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<ObjectHash, VersionFact> dag = versions.Of(spec.Package);
        VersionFact written = await WriteVersionAsync(dag, local.Root, parents, cancellationToken).ConfigureAwait(false);
        if (options.CrashAt(RunStep.Published))
            throw new RunStoppedException(RunStep.Published);
        row.BaseTree = RowBytes.Of(local.Root.Value);
        row.BaseLeaves = RowBytes.Join([written.Hash]);
        await store.SaveBindingAsync(row, CancellationToken.None).ConfigureAwait(false);
        return local.Flat;
    }

    // Steps 9 to 13 for leaves: merge them two at a time, lowest first, then write the result into the directory.
    private async Task<RunOutcome> MergeAsync(LocalState local, FlatTree diskBase, IReadOnlyList<VersionFact> leaves, SyncPlan plan, CancellationToken cancellationToken)
    {
        if (!await FetchAsync(leaves, cancellationToken).ConfigureAwait(false))
            return Waiting("a leaf's files are not stored here, and no peer supplied them yet");
        (ObjectHash target, IReadOnlyList<ObjectHash> targetLeaves)? reduced = await ReduceAsync(leaves, cancellationToken).ConfigureAwait(false);
        if (reduced is not { } result)
            return Waiting("a Tree the merge needs is not stored here");
        return await WriteTargetAsync(local, diskBase, result.target, result.targetLeaves, plan.ToString(), cancellationToken).ConfigureAwait(false);
    }

    // A first sync: the branch reduced to one leaf, the directory merged with it against the empty tree as the Version this
    // device would write next, and that merge published naming the leaf, never over it.
    private async Task<RunOutcome> FirstSyncAsync(LocalState local, SyncPlan plan, CancellationToken cancellationToken)
    {
        if (!await FetchAsync(plan.Leaves, cancellationToken).ConfigureAwait(false))
            return Waiting("a leaf's files are not stored here, and no peer supplied them yet");
        if (await ReduceAsync(plan.Leaves, cancellationToken).ConfigureAwait(false) is not { } reduced)
            return Waiting("a Tree the merge needs is not stored here");
        if (!await TreeLoader.LoadAsync(reader!, Client, spec.Package, reduced.Target, trees, cancellationToken).ConfigureAwait(false))
            return Waiting("a Tree the merge needs is not stored here");
        if (!Merge.TryOf(TreeSet.EmptyTree, trees[local.Root], trees[reduced.Target], VirtualLeaf.Key(latestClock, Own), KeyOf(reduced.Leaves[0]), trees, out MergedTree? merged, out string? problem))
            return RunOutcome.Failed(BindingState.Error, "the first sync could not merge: " + problem);

        ObjectHash target = reduced.Target;
        IReadOnlyList<ObjectHash> targetLeaves = reduced.Leaves;
        await PutAsync(local.Trees.Trees, cancellationToken).ConfigureAwait(false);
        if (merged!.Root != reduced.Target)
        {
            report(BindingState.Pushing);
            await PutAsync(merged.Made, cancellationToken).ConfigureAwait(false);
            AddMade(merged);
            VersionFact written = await WriteVersionAsync(versions.Of(spec.Package), merged.Root, reduced.Leaves, cancellationToken).ConfigureAwait(false);
            if (options.CrashAt(RunStep.MergeWritten))
                throw new RunStoppedException(RunStep.MergeWritten);
            conflicts.AddRange(merged.Copies.Select(c => Conflict(c.Path, ConflictKind.CopyWritten, c.CopyPath, c.OtherDevice, written.Hash)));
            (target, targetLeaves) = (merged.Root, [written.Hash]);
        }

        // The directory is what the merge took in: it is the base the target is written against.
        row.BaseTree = RowBytes.Of(local.Root.Value);
        return await WriteTargetAsync(local, local.Flat, target, targetLeaves, plan.ToString(), cancellationToken).ConfigureAwait(false);
    }

    // Merges leaves two at a time, the two lowest in the total order first, until one is left (SPEC.md §9.5). A merge that equals
    // the later of the last two leaves is not written (both stay the base's leaves, and the next publish names both); a read-only
    // binding writes none and merges in memory only.
    private async Task<(ObjectHash Target, IReadOnlyList<ObjectHash> Leaves)?> ReduceAsync(IReadOnlyList<VersionFact> leaves, CancellationToken cancellationToken)
    {
        var dag = new Dictionary<ObjectHash, VersionFact>(versions.Of(spec.Package));
        List<VersionFact> current = [.. leaves.OrderBy(l => l.Key)];
        while (current.Count > 1)
        {
            (VersionFact first, VersionFact second) = (current[0], current[1]);
            VersionFact? ancestor = Ancestor.Of(dag, first, second);
            foreach (ObjectHash root in new[] { first.Tree, second.Tree }.Concat(ancestor is null ? [] : [ancestor.Tree]))
            {
                if (!await TreeLoader.LoadAsync(reader!, Client, spec.Package, root, trees, cancellationToken).ConfigureAwait(false))
                    return null;
            }

            Tree ancestorTree = ancestor is null ? TreeSet.EmptyTree : trees[ancestor.Tree];
            if (!Merge.TryOf(ancestorTree, trees[first.Tree], trees[second.Tree], first.Key, second.Key, trees, out MergedTree? merged, out string? problem))
                throw new IOException("The merge failed: " + problem);
            AddMade(merged!);
            if (merged!.EqualsLater && current.Count == 2)
                return (merged.Root, [first.Hash, second.Hash]);

            VersionFact made;
            if (readOnly)
            {
                made = new VersionFact(merged.Root, new EntryKey(Math.Max(first.Key.Clock, second.Key.Clock) + 1, Own, merged.Root), [first.Hash, second.Hash], spec.Label, merged.Root);
            }
            else
            {
                report(BindingState.Pushing);
                await PutAsync(merged.Made, cancellationToken).ConfigureAwait(false);
                made = await WriteVersionAsync(dag, merged.Root, [first.Hash, second.Hash], cancellationToken).ConfigureAwait(false);
                if (options.CrashAt(RunStep.MergeWritten))
                    throw new RunStoppedException(RunStep.MergeWritten);
            }

            conflicts.AddRange(merged.Copies.Select(c => Conflict(c.Path, ConflictKind.CopyWritten, c.CopyPath, c.OtherDevice, readOnly ? default : made.Hash)));
            dag[made.Hash] = made;
            current = [.. Leaves.Of(dag, spec.Label).Where(l => current.Any(c => c.Hash == l.Hash) || l.Hash == made.Hash)];
        }

        return (current[0].Tree, [current[0].Hash]);
    }

    // Steps 10 to 12: record the target as pending, write it into the directory, commit it as the base.
    private async Task<RunOutcome> WriteTargetAsync(LocalState local, FlatTree diskBase, ObjectHash target, IReadOnlyList<ObjectHash> targetLeaves, string what, CancellationToken cancellationToken)
    {
        if (!await TreeLoader.LoadAsync(reader!, Client, spec.Package, target, trees, cancellationToken).ConfigureAwait(false) || !TreeFlattener.TryOf(trees[target], trees, out FlatTree targetFlat, out _))
            return Waiting("a Tree of the target is not stored here");
        row.PendingTree = RowBytes.Of(target.Value);
        row.PendingLeaves = RowBytes.Join(targetLeaves);
        await store.SaveBindingAsync(row, CancellationToken.None).ConfigureAwait(false);
        if (options.CrashAt(RunStep.PendingRecorded))
            throw new RunStoppedException(RunStep.PendingRecorded);

        report(BindingState.Pulling);
        DiskPlan disk = DiskDiff.Of(Visible(diskBase), Visible(targetFlat), Visible(local.Flat), local.Busy, windows);
        (Status status, IReadOnlyList<string> faulty) = await new Materialiser(reader!, store, spec, options.CrashAt).RunAsync(disk, cancellationToken).ConfigureAwait(false);
        if (status != Status.Ok)
            return Waiting("a file of the target is not stored here");
        conflicts.AddRange(disk.Refused.Select(p => Conflict(p, ConflictKind.NameRefused, string.Empty, default, default)));
        conflicts.AddRange(faulty.Select(p => Conflict(p, ConflictKind.Unreadable, string.Empty, default, default)));

        row.BaseTree = RowBytes.Of(target.Value);
        row.BaseLeaves = RowBytes.Join(targetLeaves);
        row.PendingTree = null;
        row.PendingLeaves = [];
        await store.SaveBindingAsync(row, CancellationToken.None).ConfigureAwait(false);
        if (options.CrashAt(RunStep.BaseCommitted))
            throw new RunStoppedException(RunStep.BaseCommitted);
        uint skipped = (uint)disk.Skipped.Count;
        BindingState state = readOnly && skipped > 0 ? BindingState.LocalChangesReadOnly : BindingState.InSync;
        return await EndedAsync(state, what, skipped, targetFlat, local.HasBusy).ConfigureAwait(false);
    }

    private async Task<VersionFact> WriteVersionAsync(IReadOnlyDictionary<ObjectHash, VersionFact> dag, ObjectHash tree, IReadOnlyList<ObjectHash> parents, CancellationToken cancellationToken)
    {
        Entry? entry = await Publisher.WriteAsync(Client, dag, spec.Package, spec.Label, tree, parents, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return Publisher.OwnLeaf(dag, spec.Label, Own, tree)!;
        await versions.TakeInAsync([entry], fromStream: false, CancellationToken.None).ConfigureAwait(false);
        return VersionFact.TryOf(entry, out VersionFact? fact) ? fact : throw new IOException("The Host stored a Version this service cannot read back.");
    }

    private async Task<bool> FetchAsync(IEnumerable<VersionFact> leaves, CancellationToken cancellationToken)
    {
        foreach (VersionFact leaf in leaves)
        {
            if (!await ObjectFetcher.FetchAsync(Client, spec.Package, leaf.Tree, cancellationToken).ConfigureAwait(false))
                return false;
        }

        return true;
    }

    private async Task PutAsync(IEnumerable<Tree> made, CancellationToken cancellationToken)
    {
        IReadOnlyList<ObjectHash> put = await Publisher.PutTreesAsync(Client, made, cancellationToken).ConfigureAwait(false);
        await store.AddPutsAsync(spec.Id, put, CancellationToken.None).ConfigureAwait(false);
    }

    private void AddMade(MergedTree merged)
    {
        foreach (Tree tree in merged.Made)
            trees[tree.Hash] = tree;
    }

    private async Task ReleasePutsAsync()
    {
        try
        {
            IReadOnlyList<ObjectHash> held = await store.PutsAsync(spec.Id, CancellationToken.None).ConfigureAwait(false);
            foreach (ObjectHash[] batch in held.Chunk(1_024))
            {
                LocalResult<ushort> released = await Client.SendAsync(new Unpin { Hashes = batch }, CancellationToken.None).ConfigureAwait(false);
                if (released.IsOk)
                    await store.RemovePutsAsync(spec.Id, batch, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is LocalConnectionException or OperationCanceledException or IOException)
        {
            // The connection ended: the rows stay, and the next run releases them.
        }
    }

    // Symlinks of the branch a binding does not write are left out of every state the disk plan compares.
    private FlatTree Visible(FlatTree flat) =>
        spec.Symlinks == SymlinkPolicy.Ignore ? FlatTree.Of(flat.Entries.Where(e => e.Value.Mode != TreeEntryMode.Symlink)) : flat;

    private async Task<RunOutcome> EndedAsync(BindingState state, string what, uint localChanges, FlatTree directory, bool busy)
    {
        IReadOnlyList<ConflictRow> recorded = conflicts.Count == 0 ? [] : await store.AddConflictsAsync([.. conflicts], CancellationToken.None).ConfigureAwait(false);
        conflicts.Clear();
        ulong bytes = directory.Files.Aggregate(0ul, (sum, f) => sum + f.Value.Size);
        return new RunOutcome(state, string.Empty, what, false, busy, localChanges, (ulong)directory.Files.Count(), bytes, recorded);
    }

    private RunOutcome Waiting(string why) => RunOutcome.Failed(BindingState.WaitingForObjects, why);

    private EntryKey KeyOf(ObjectHash version) => versions.Of(spec.Package).TryGetValue(version, out VersionFact? fact) ? fact.Key : new EntryKey(0, default, version);

    private ConflictRow Conflict(string path, ConflictKind kind, string copyPath, DeviceId other, ObjectHash version) => new()
    {
        Binding = spec.Id,
        Path = path,
        Kind = (byte)kind,
        CopyPath = copyPath,
        OtherDevice = RowBytes.Of(other.Value),
        Version = RowBytes.Of(version.Value),
        AtMs = options.Time.GetUtcNow().ToUnixTimeMilliseconds(),
    };

    // Files of the directory that differ from the base: a read-only binding's local changes.
    private static uint Changes(FlatTree baseFlat, FlatTree local) =>
        (uint)local.Files.Count(f => baseFlat.At(f.Key) != f.Value) + (uint)baseFlat.Files.Count(f => local.At(f.Key) is null);

    // The directory as one run sees it.
    private sealed record LocalState(FlatTree Flat, FlatTree Base, TreeSet Trees, IReadOnlySet<string> Busy, bool HasBusy, string? Problem, BindingState ProblemState)
    {
        public ObjectHash Root => Trees.Root;

        public static LocalState Failed(BindingState state, string problem) => new(FlatTree.Empty, FlatTree.Empty, null!, new HashSet<string>(), false, problem, state);
    }
}
