using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>An event in a subscription's queue, with the size of its frame, counted once (LOCAL-SPEC.md §7.4).</summary>
/// <param name="Event">The event, shared by every subscription that receives it.</param>
/// <param name="Size">The encoded size of its frame.</param>
/// <param name="StoreSeq">Topic entries: the Entry's store sequence; else 0.</param>
internal readonly record struct QueuedEvent(LocalEvent Event, long Size, ulong StoreSeq);
