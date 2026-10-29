namespace Axiom.Mesh.Core;

/// <summary>Records that an object was read or served: eviction goes least-recently-used first (SPEC.md §11.3). Fire and forget.</summary>
internal sealed class TouchCommand : StoreCommand<bool>
{
    /// <summary>A use of an object.</summary>
    /// <param name="hash">The object.</param>
    public TouchCommand(ObjectHash hash) => Hash = hash;

    /// <summary>The object.</summary>
    public ObjectHash Hash { get; }
}
