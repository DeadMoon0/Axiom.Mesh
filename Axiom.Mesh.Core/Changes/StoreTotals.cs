namespace Axiom.Mesh.Core;

/// <summary>The store's totals after a batch (LOCAL-SPEC.md §5.10).</summary>
/// <param name="Objects">Stored objects.</param>
/// <param name="Bytes">Bytes of stored objects.</param>
/// <param name="PinnedBytes">Bytes of objects with at least one pin.</param>
/// <param name="Budget">The budget eviction keeps the store under.</param>
/// <param name="StoreSeq">The last store sequence assigned.</param>
internal sealed record StoreTotals(long Objects, long Bytes, long PinnedBytes, long Budget, ulong StoreSeq)
{
    /// <summary>Bytes of objects with no pin: what eviction may remove.</summary>
    public long EvictableBytes => Bytes - PinnedBytes;
}
