namespace Axiom.Mesh.Core;

/// <summary>What one eviction pass did.</summary>
/// <param name="Evicted">Objects evicted.</param>
/// <param name="EvictedBytes">Their bytes.</param>
/// <param name="Later">Objects a reader held, left for a later pass.</param>
/// <param name="UnderBudget">Whether the store is within its budget afterwards.</param>
internal readonly record struct EvictionReport(int Evicted, long EvictedBytes, int Later, bool UnderBudget);
