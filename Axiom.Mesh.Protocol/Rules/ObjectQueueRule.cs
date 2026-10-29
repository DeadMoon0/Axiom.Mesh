using System;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// The bound on <c>Object</c> payload queued toward one peer (SPEC.md §7.6): at most 8 MiB of object bytes. An answer
/// that does not fit is deferred, not dropped; the session keeps reading, so <c>Ping</c>, <c>Pong</c> and
/// <c>Cancel</c> still flow, and a deferred want still counts against <see cref="WantBudget"/>. An immutable value.
/// </summary>
/// <remarks>The bytes counted are the objects' own (the <c>object</c> field), not the frame around them.</remarks>
public readonly struct ObjectQueueRule
{
    private ObjectQueueRule(long queuedBytes) => QueuedBytes = queuedBytes;

    /// <summary>Nothing queued: the state of a new session.</summary>
    public static ObjectQueueRule Empty => default;

    /// <summary>Object bytes queued and not yet written to the peer.</summary>
    public long QueuedBytes { get; }

    /// <summary>Whether wants are being deferred: the queue is full.</summary>
    public bool DefersWants => QueuedBytes >= Limits.MaxQueuedObjectBytes;

    /// <summary>Queues an object to answer a want, if it fits.</summary>
    /// <param name="objectBytes">The object's length, at most 4 MiB.</param>
    /// <param name="next">The queue with it; unchanged when it does not fit.</param>
    /// <returns><see langword="false"/> when queueing it would pass 8 MiB: defer the want until the queue drains.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The length is negative or above 4 MiB.</exception>
    public bool TryQueue(int objectBytes, out ObjectQueueRule next)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(objectBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(objectBytes, Limits.MaxObjectLength);
        bool fits = QueuedBytes + objectBytes <= Limits.MaxQueuedObjectBytes;
        next = fits ? new ObjectQueueRule(QueuedBytes + objectBytes) : this;
        return fits;
    }

    /// <summary>Removes an object that was written to the peer, or dropped after a <c>Cancel</c>.</summary>
    /// <param name="objectBytes">The object's length.</param>
    /// <returns>The queue without it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Negative, or more than is queued: the caller lost count.</exception>
    public ObjectQueueRule Dequeue(int objectBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(objectBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)objectBytes, QueuedBytes, nameof(objectBytes));
        return new ObjectQueueRule(QueuedBytes - objectBytes);
    }

    /// <inheritdoc />
    public override string ToString() => $"{QueuedBytes} of {Limits.MaxQueuedObjectBytes} object bytes queued";
}
