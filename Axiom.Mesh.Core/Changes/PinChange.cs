namespace Axiom.Mesh.Core;

/// <summary>A pin added or removed by a batch (LOCAL-SPEC.md §5.11).</summary>
/// <param name="Hash">The pinned object.</param>
/// <param name="Reason">Why.</param>
/// <param name="Source">Who for.</param>
/// <param name="Added"><see langword="true"/> when added, <see langword="false"/> when removed.</param>
internal sealed record PinChange(ObjectHash Hash, PinReason Reason, Hash32 Source, bool Added);
