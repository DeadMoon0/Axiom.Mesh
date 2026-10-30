using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The work of LOCAL-SPEC.md §6.4 <c>Fetch</c>, which <c>Materialise</c> shares: the object and, when recursive,
/// everything it references through Trees and ChunkLists, wanted from the peers that list the Package (SPEC.md §10.4)
/// until it is stored. An object already stored counts at once. When, for 60 seconds, no established peer is a possible
/// source for an object still lacking, the fetch ends <c>NotFound</c> naming it; until then it waits. A cancelled fetch
/// cancels its outstanding wants (§8.5) and keeps what was stored.
/// </summary>
internal sealed class ObjectFetcher
{
    private static readonly TimeSpan WithoutSource = TimeSpan.FromSeconds(Limits.FetchWithoutSourceSeconds);
    private static readonly TimeSpan Look = TimeSpan.FromSeconds(1);

    private readonly MeshNode node;
    private readonly PackageId package;
    private readonly bool recursive;
    private readonly Func<ObjectHash, long, CancellationToken, Task<bool>> stored;
    private readonly HashSet<ObjectHash> visited = [];
    private readonly HashSet<ObjectHash> missing = [];
    private readonly Queue<ObjectHash> frontier = new();
    private uint objects;
    private ulong bytes;

    /// <summary>A fetch through a Package.</summary>
    /// <param name="node">The node.</param>
    /// <param name="package">The followed Package whose peers are asked.</param>
    /// <param name="recursive">Also every object referenced through Trees and ChunkLists.</param>
    /// <param name="stored">Called for each object once it is stored, with its size; <see langword="false"/> stops the fetch with <c>StoreFull</c> (a pin over the budget).</param>
    public ObjectFetcher(MeshNode node, PackageId package, bool recursive, Func<ObjectHash, long, CancellationToken, Task<bool>> stored)
    {
        this.node = node;
        this.package = package;
        this.recursive = recursive;
        this.stored = stored;
    }

    /// <summary>Objects stored so far, counted once each.</summary>
    public uint Fetched => objects;

    /// <summary>Their bytes.</summary>
    public ulong Bytes => bytes;

    /// <summary>Fetches until everything is stored, no source is left for 60 seconds, or the work is cancelled.</summary>
    /// <param name="root">The object.</param>
    /// <param name="progress">Where progress is reported, in objects.</param>
    /// <param name="cancellationToken">The operation's token.</param>
    /// <returns><see langword="null"/> when everything is stored; else the outcome to fail with.</returns>
    public async Task<OperationOutcome?> RunAsync(ObjectHash root, OperationProgress progress, CancellationToken cancellationToken)
    {
        using ChangeSubscription changes = node.Replicating.Bus.Subscribe(4096);
        WantScheduler wants = node.Replicating.Wants;
        TimeProvider time = node.Time;
        frontier.Enqueue(root);
        long? sourceless = null;
        Task<bool>? reading = null;
        bool following = true;
        try
        {
            while (true)
            {
                if (await DrainAsync(cancellationToken).ConfigureAwait(false) is { } full)
                {
                    wants.CancelFetch(missing);
                    return full;
                }

                if (missing.Count == 0)
                    return null;

                List<PlannedWant> planned = [.. missing.Select(h => new PlannedWant(h, WantKind.Fetch))];
                wants.Want(planned, package, null);
                foreach (DeviceId peer in node.Replicating.ListedBy(package))
                    wants.Want(planned, package, peer);

                ObjectHash? lacking = missing.Where(h => !wants.HasSource(h)).Select(h => (ObjectHash?)h).FirstOrDefault();
                TimeSpan wait = Look;
                string message = $"fetching {missing.Count} objects";
                if (lacking is { } without)
                {
                    sourceless ??= time.GetTimestamp();
                    TimeSpan waited = time.GetElapsedTime(sourceless.Value);
                    if (waited >= WithoutSource)
                    {
                        wants.CancelFetch(missing);
                        return OperationOutcome.Failed(Status.NotFound, $"No peer can supply {without}: none is connected that lists the Package, or every one answered NotFound.");
                    }

                    wait = WithoutSource - waited < Look ? WithoutSource - waited : Look;
                    message = $"waiting for a source of {without}";
                }
                else
                {
                    sourceless = null;
                }

                // The next look is armed before progress says where the fetch stands, so a clock moved after it finds it.
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (following)
                    reading ??= changes.Reader.WaitToReadAsync(cancellationToken).AsTask();
                Task due = Task.Delay(wait, time, tick.Token);
                progress.Report(objects, objects + (ulong)missing.Count, message);
                await (reading is null ? due : Task.WhenAny(reading, due)).ConfigureAwait(false);
                await tick.CancelAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (reading is { IsCompleted: true })
                {
                    following = await reading.ConfigureAwait(false);
                    reading = null;
                }

                Arrived(changes, following);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            wants.CancelFetch(missing);
            throw;
        }
    }

    // Every object of the frontier: counted when stored (and its children queued), else lacking.
    private async Task<OperationOutcome?> DrainAsync(CancellationToken cancellationToken)
    {
        while (frontier.TryDequeue(out ObjectHash hash))
        {
            if (!visited.Add(hash))
                continue;
            if (await StoredObjects.ReadAsync(node.Files, hash, cancellationToken).ConfigureAwait(false) is not { } held)
            {
                missing.Add(hash);
                continue;
            }

            missing.Remove(hash);
            objects++;
            bytes += (ulong)held.Length;
            if (!await stored(hash, held.Length, cancellationToken).ConfigureAwait(false))
                return OperationOutcome.Failed(Status.StoreFull, $"Pinning {hash} would put the pinned bytes over the budget.");
            if (recursive)
            {
                foreach (ObjectHash child in ChildrenOf(held, hash))
                    frontier.Enqueue(child);
            }
        }

        return null;
    }

    // A stored object that was lacking goes back to the frontier to be counted; once the subscription lapsed, every
    // lacking object is looked at again on each look.
    private void Arrived(ChangeSubscription changes, bool following)
    {
        while (changes.Reader.TryRead(out ChangeSet? set))
        {
            foreach (StoredObject arrived in set.Stored)
            {
                if (missing.Contains(arrived.Hash) && visited.Remove(arrived.Hash))
                    frontier.Enqueue(arrived.Hash);
            }
        }

        if (!following || changes.Lapsed)
        {
            foreach (ObjectHash hash in missing.Where(visited.Remove).ToList())
                frontier.Enqueue(hash);
        }
    }

    private static IEnumerable<ObjectHash> ChildrenOf(byte[] held, ObjectHash hash)
    {
        if (!HashedObject.TryVerify(held, hash, out HashedObject verified) || !Objects.TryParse(verified, out ParsedObject? parsed, out _))
            return [];
        return parsed switch
        {
            Tree tree => tree.Entries.Select(e => e.Hash).Distinct(),
            ChunkList list => list.Chunks.Distinct(),
            _ => [],
        };
    }
}
