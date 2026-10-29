using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Core;

/// <summary>
/// The held buffer of SPEC.md §10.6: objects received before what they need, kept for reordering, not for trust. At
/// most 8 MiB per sender and 64 MiB in all, the oldest dropped beyond that; whatever is still held 10 minutes after
/// its sender disconnected is dropped. Timestamps come in from the caller, so the buffer is deterministic; an object
/// already held is not held twice. Thread-safe.
/// </summary>
internal sealed class HeldBuffer
{
    private readonly object gate = new();
    private readonly List<HeldObject> items = [];
    private readonly HashSet<ObjectHash> hashes = [];
    private readonly Dictionary<Origin, long> bytesByOrigin = [];
    private readonly Dictionary<Origin, long> goneSince = [];
    private readonly long perOrigin;
    private readonly long total;
    private readonly long goneMs;
    private long bytes;

    /// <summary>A buffer with the bounds of SPEC.md §10.6.</summary>
    public HeldBuffer()
        : this(Limits.MaxHeldBytesPerSession, Limits.MaxHeldBytes, Limits.HeldAfterDisconnectSeconds * 1000L)
    {
    }

    private HeldBuffer(long perOrigin, long total, long goneMs)
    {
        this.perOrigin = perOrigin;
        this.total = total;
        this.goneMs = goneMs;
    }

    /// <summary>Bytes held in all.</summary>
    public long Bytes
    {
        get
        {
            lock (gate)
                return bytes;
        }
    }

    /// <summary>Objects held.</summary>
    public int Count
    {
        get
        {
            lock (gate)
                return items.Count;
        }
    }

    /// <summary>Bytes held from one sender.</summary>
    /// <param name="origin">The sender.</param>
    /// <returns>The bytes.</returns>
    public long BytesOf(Origin origin)
    {
        lock (gate)
            return bytesByOrigin.GetValueOrDefault(origin);
    }

    /// <summary>Whether an object is held.</summary>
    /// <param name="hash">The object's hash.</param>
    /// <returns><see langword="true"/> when held.</returns>
    public bool Contains(ObjectHash hash)
    {
        lock (gate)
            return hashes.Contains(hash);
    }

    /// <summary>Holds an object, dropping the oldest of its sender beyond 8 MiB and the oldest of all beyond 64 MiB.</summary>
    /// <param name="item">The object.</param>
    /// <returns>What was dropped to make room, or the object itself when it is held already.</returns>
    public IReadOnlyList<HeldObject> Add(HeldObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var dropped = new List<HeldObject>();
        lock (gate)
        {
            if (!hashes.Add(item.Object.Hash))
                return [item];

            items.Add(item);
            Tally(item, +1);
            while (bytesByOrigin.GetValueOrDefault(item.Origin) > perOrigin)
                dropped.Add(RemoveAt(items.FindIndex(i => i.Origin == item.Origin)));
            while (bytes > total)
                dropped.Add(RemoveAt(0));
        }

        return dropped;
    }

    /// <summary>Takes out every held object <paramref name="ready"/> accepts, oldest first.</summary>
    /// <param name="ready">Whether an object can now be decided.</param>
    /// <returns>The objects taken.</returns>
    public IReadOnlyList<HeldObject> Take(Func<HeldObject, bool> ready)
    {
        ArgumentNullException.ThrowIfNull(ready);
        lock (gate)
        {
            List<HeldObject> taken = [.. items.Where(ready)];
            foreach (HeldObject item in taken)
                RemoveAt(items.IndexOf(item));
            return taken;
        }
    }

    /// <summary>A sender disconnected at <paramref name="nowMs"/>: what it sent is dropped 10 minutes later unless it reconnects.</summary>
    /// <param name="origin">The sender.</param>
    /// <param name="nowMs">The time, in milliseconds since the Unix epoch.</param>
    public void OriginGone(Origin origin, long nowMs)
    {
        lock (gate)
            goneSince.TryAdd(origin, nowMs);
    }

    /// <summary>A sender connected again: what it sent stays held.</summary>
    /// <param name="origin">The sender.</param>
    public void OriginBack(Origin origin)
    {
        lock (gate)
            goneSince.Remove(origin);
    }

    /// <summary>Drops every object whose sender has been gone for 10 minutes or more.</summary>
    /// <param name="nowMs">The time, in milliseconds since the Unix epoch.</param>
    /// <returns>What was dropped.</returns>
    public IReadOnlyList<HeldObject> Expire(long nowMs)
    {
        lock (gate)
        {
            HashSet<Origin> expired = [.. goneSince.Where(g => nowMs - g.Value >= goneMs).Select(g => g.Key)];
            if (expired.Count == 0)
                return [];

            List<HeldObject> dropped = [.. items.Where(i => expired.Contains(i.Origin))];
            foreach (HeldObject item in dropped)
                RemoveAt(items.IndexOf(item));
            foreach (Origin origin in expired)
                goneSince.Remove(origin);
            return dropped;
        }
    }

    private HeldObject RemoveAt(int index)
    {
        HeldObject item = items[index];
        items.RemoveAt(index);
        hashes.Remove(item.Object.Hash);
        Tally(item, -1);
        return item;
    }

    private void Tally(HeldObject item, int sign)
    {
        long left = bytesByOrigin.GetValueOrDefault(item.Origin) + (sign * item.Bytes);
        if (left == 0)
            bytesByOrigin.Remove(item.Origin);
        else
            bytesByOrigin[item.Origin] = left;
        bytes += sign * item.Bytes;
    }
}
