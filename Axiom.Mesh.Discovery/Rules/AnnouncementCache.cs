using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Axiom.Mesh.Discovery;

/// <summary>
/// The newest claim per device, bounded. This is discovery's defence against spam (DESIGN.md §14): an announcement of
/// a device nobody knows costs one entry here and nothing else, and when the cache is full the entry heard of least
/// recently is evicted, so a thousand forged devices cost <see cref="Capacity"/> entries. Takes timestamps, holds no clock.
/// </summary>
/// <remarks>
/// Claims are keyed by device id; a static peer, which claims no id, is keyed by its address. Thread-safe.
/// </remarks>
public sealed class AnnouncementCache
{
    private readonly Dictionary<(DeviceId Device, IPEndPoint? Address), LinkedListNode<Entry>> entries = [];

    // Least recently heard first: the eviction order.
    private readonly LinkedList<Entry> byLastHeard = new();
    private readonly object sync = new();

    /// <summary>Creates an empty cache.</summary>
    /// <param name="capacity">The most claims held; at least 1.</param>
    /// <param name="repeatAfter">How long a claim passed on stays a <see cref="AnnouncementNovelty.Duplicate"/>; not negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">A bound is out of range.</exception>
    public AnnouncementCache(int capacity, TimeSpan repeatAfter)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The cache must hold at least one claim.");
        if (repeatAfter < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(repeatAfter), repeatAfter, "The repeat window cannot be negative.");

        Capacity = capacity;
        RepeatAfter = repeatAfter;
    }

    /// <summary>The most claims held.</summary>
    public int Capacity { get; }

    /// <summary>How long a claim passed on stays a duplicate.</summary>
    public TimeSpan RepeatAfter { get; }

    /// <summary>The claims held now.</summary>
    public int Count
    {
        get
        {
            lock (sync)
                return entries.Count;
        }
    }

    /// <summary>
    /// Records an announcement heard at <paramref name="timestamp"/> and says what it adds. The newest claim of a device
    /// wins; the mechanisms that reported the same claim are merged into its <see cref="Announcement.Source"/>.
    /// </summary>
    /// <param name="announcement">What was heard.</param>
    /// <param name="timestamp">Monotonic time of hearing it.</param>
    /// <returns>Whether it is new, changed, repeated or a duplicate; pass it on unless a duplicate.</returns>
    public AnnouncementNovelty Observe(Announcement announcement, TimeSpan timestamp)
    {
        ArgumentNullException.ThrowIfNull(announcement);
        var key = KeyOf(announcement);
        lock (sync)
        {
            if (!entries.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                if (entries.Count == Capacity)
                    Evict();

                entries[key] = byLastHeard.AddLast(new Entry(key, announcement, timestamp));
                return AnnouncementNovelty.New;
            }

            Entry entry = node.Value;
            byLastHeard.Remove(node);
            byLastHeard.AddLast(node);
            if (!entry.Claim.ClaimsTheSameAs(announcement))
            {
                entry.Claim = announcement;
                entry.LastPassed = timestamp;
                return AnnouncementNovelty.Changed;
            }

            entry.Claim = entry.Claim with { Source = entry.Claim.Source | announcement.Source };
            if (timestamp - entry.LastPassed < RepeatAfter)
                return AnnouncementNovelty.Duplicate;

            entry.LastPassed = timestamp;
            return AnnouncementNovelty.Repeated;
        }
    }

    /// <summary>The newest claim of a device, with every mechanism that reported it.</summary>
    /// <param name="device">The device id.</param>
    /// <param name="announcement">The claim, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the cache holds a claim of that device.</returns>
    public bool TryGet(DeviceId device, [NotNullWhen(true)] out Announcement? announcement)
    {
        lock (sync)
        {
            announcement = entries.TryGetValue((device, null), out LinkedListNode<Entry>? node) ? node.Value.Claim : null;
            return announcement is not null;
        }
    }

    /// <summary>Every claim held, least recently heard first.</summary>
    /// <returns>A copy; later observations do not change it.</returns>
    public IReadOnlyList<Announcement> Snapshot()
    {
        lock (sync)
        {
            var claims = new List<Announcement>(entries.Count);
            foreach (Entry entry in byLastHeard)
                claims.Add(entry.Claim);
            return claims;
        }
    }

    private static (DeviceId Device, IPEndPoint? Address) KeyOf(Announcement announcement) =>
        announcement.Device.Value.IsZero ? (announcement.Device, announcement.Address) : (announcement.Device, null);

    private void Evict()
    {
        LinkedListNode<Entry> oldest = byLastHeard.First!;
        byLastHeard.RemoveFirst();
        entries.Remove(oldest.Value.Key);
    }

    private sealed class Entry((DeviceId Device, IPEndPoint? Address) key, Announcement claim, TimeSpan lastPassed)
    {
        public (DeviceId Device, IPEndPoint? Address) Key { get; } = key;

        public Announcement Claim { get; set; } = claim;

        public TimeSpan LastPassed { get; set; } = lastPassed;
    }
}
