namespace Axiom.Mesh.Core;

/// <summary>
/// Evicts unpinned, unpromised objects least-recently-used first until the store is within its budget (SPEC.md §11.3).
/// </summary>
internal sealed class EvictUnderBudgetCommand : StoreCommand<EvictionReport>;
