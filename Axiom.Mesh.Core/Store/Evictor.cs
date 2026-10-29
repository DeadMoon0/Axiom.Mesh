using System;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core;

/// <summary>
/// Keeps the store within its budget (SPEC.md §11.3): whenever a committed batch leaves more bytes than the budget, it
/// asks the writer to evict unpinned, unpromised objects least-recently-used first. Never a pinned or promised object;
/// what it evicts appears in the next change set, after which no node claims it.
/// </summary>
internal sealed class Evictor
{
    private const int Backlog = 64;
    private readonly StoreWriter writer;
    private readonly ChangeBus bus;

    /// <summary>An evictor for a writer.</summary>
    /// <param name="writer">The store writer.</param>
    /// <param name="bus">The writer's change bus.</param>
    public Evictor(StoreWriter writer, ChangeBus bus)
    {
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    /// <summary>One eviction pass now.</summary>
    /// <param name="cancellationToken">Cancels waiting for room in the writer's queue.</param>
    /// <returns>What the pass did.</returns>
    public Task<EvictionReport> EvictNowAsync(CancellationToken cancellationToken) =>
        writer.SendAsync(new EvictUnderBudgetCommand(), cancellationToken);

    /// <summary>Evicts after every batch that leaves the store over budget, until cancelled.</summary>
    /// <param name="cancellationToken">Stops the evictor.</param>
    /// <returns>A task that completes when stopped.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using ChangeSubscription changes = bus.Subscribe(Backlog);
            if (writer.Totals.Bytes > writer.Totals.Budget)
                await EvictNowAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await foreach (ChangeSet set in changes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (set.Totals.Bytes > set.Totals.Budget && set.Evicted.Count == 0)
                        await EvictNowAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
