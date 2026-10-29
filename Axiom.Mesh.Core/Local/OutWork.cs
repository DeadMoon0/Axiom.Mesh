namespace Axiom.Mesh.Core;

/// <summary>
/// The next thing a connection's writer writes: a control frame, a queued event of a subscription, or the next Entry a
/// replay reads from the store (LOCAL-SPEC.md §7.4).
/// </summary>
/// <param name="Control">A control frame; <see langword="null"/> for an event or a replay step.</param>
/// <param name="Subscription">The subscription of an event or a replay step.</param>
/// <param name="Event">The queued event, when <see cref="ReplaySeq"/> is 0.</param>
/// <param name="ReplaySeq">The store sequence a replay reads next; 0 for a queued event.</param>
internal readonly record struct OutWork(OutFrame? Control, LocalSubscription? Subscription, QueuedEvent Event, ulong ReplaySeq);
