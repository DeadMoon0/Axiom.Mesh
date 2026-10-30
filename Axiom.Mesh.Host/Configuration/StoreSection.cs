namespace Axiom.Mesh.Host;

/// <summary><c>store</c>: the object store (LOCAL-SPEC.md §5.10, §6.5).</summary>
internal sealed record StoreSection
{
    /// <summary>The budget in bytes eviction keeps the store under; 0 for none. Default 0.</summary>
    public long BudgetBytes { get; set; }
}
