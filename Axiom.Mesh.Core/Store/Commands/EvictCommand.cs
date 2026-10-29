namespace Axiom.Mesh.Core;

/// <summary>Evicts one object unless a pin or an outstanding promise holds it (SPEC.md §11.3).</summary>
internal sealed class EvictCommand : StoreCommand<EvictionOutcome>
{
    /// <summary>An eviction of one object.</summary>
    /// <param name="hash">The object.</param>
    public EvictCommand(ObjectHash hash) => Hash = hash;

    /// <summary>The object.</summary>
    public ObjectHash Hash { get; }
}
