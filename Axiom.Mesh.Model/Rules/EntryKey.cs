using System;

namespace Axiom.Mesh;

/// <summary>
/// An Entry's position in its Package's total order: by clock, then device id, then hash, each ascending, the ids and
/// the hash as unsigned byte strings (SPEC.md §9.3; LOCAL-SPEC.md §5.6). Every node that holds the same Entries orders
/// them identically.
/// </summary>
/// <param name="Clock">The Entry's Lamport clock.</param>
/// <param name="Device">The writing device.</param>
/// <param name="Hash">The Entry's object hash.</param>
[ClosedShape("The total order has exactly these three keys (SPEC.md §9.3).")]
public readonly record struct EntryKey(ulong Clock, DeviceId Device, ObjectHash Hash) : IComparable<EntryKey>
{
    /// <summary>Compares by clock, then device id, then hash.</summary>
    /// <param name="other">The other key.</param>
    /// <returns>Negative, zero or positive.</returns>
    public int CompareTo(EntryKey other)
    {
        int byClock = Clock.CompareTo(other.Clock);
        if (byClock != 0)
            return byClock;
        int byDevice = Device.CompareTo(other.Device);
        return byDevice != 0 ? byDevice : Hash.CompareTo(other.Hash);
    }

    /// <summary>Whether <paramref name="left"/> comes before <paramref name="right"/>.</summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    /// <returns>The comparison.</returns>
    public static bool operator <(EntryKey left, EntryKey right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> comes after <paramref name="right"/>.</summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    /// <returns>The comparison.</returns>
    public static bool operator >(EntryKey left, EntryKey right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> comes before or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    /// <returns>The comparison.</returns>
    public static bool operator <=(EntryKey left, EntryKey right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> comes after or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    /// <returns>The comparison.</returns>
    public static bool operator >=(EntryKey left, EntryKey right) => left.CompareTo(right) >= 0;
}
