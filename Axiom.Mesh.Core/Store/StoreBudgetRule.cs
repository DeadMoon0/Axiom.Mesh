namespace Axiom.Mesh.Core;

/// <summary>
/// LOCAL-SPEC.md §6.5: a local request may store an object only while the pinned bytes plus the new object stay within
/// the budget; eviction cannot help, because pinned objects are not evicted.
/// </summary>
internal static class StoreBudgetRule
{
    /// <summary>Whether an object of <paramref name="bytes"/> may be stored on a local request.</summary>
    /// <param name="totals">The store's totals now.</param>
    /// <param name="bytes">The new object's size.</param>
    /// <returns><see langword="false"/> for <c>StoreFull</c>.</returns>
    public static bool Admits(StoreTotals totals, long bytes) => totals.PinnedBytes + bytes <= totals.Budget;
}
