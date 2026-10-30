using System;

namespace Axiom.Mesh.Core;

/// <summary>
/// Sets the store's budget (LOCAL-SPEC.md §6.5 <c>SetStoreBudget</c>): it applies from the batch on, and a store above it
/// is evicted down to it (SPEC.md §11.3). Completes with the budget in force. Not persisted: the configured budget applies
/// again at the next start (STRUCTURAL-DEBT.md 38).
/// </summary>
internal sealed class BudgetCommand : StoreCommand<long>
{
    /// <summary>A new budget.</summary>
    /// <param name="bytes">The budget in bytes; positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bytes"/> is not positive.</exception>
    public BudgetCommand(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        Budget = bytes;
    }

    /// <summary>The budget in bytes.</summary>
    public long Budget { get; }
}
