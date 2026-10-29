using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Axiom.Mesh.Core;

/// <summary>
/// The stored Heads of every Package (SPEC.md §9.3), one per writing device, ascending by device id: what a listing and
/// a <c>PackageChanged</c> carry (§7.2, §7.3) and what the planner compares a peer's Heads with (§10.2). Immutable; the
/// store writer swaps a new view in after every batch that moved a Head, so sessions read it without a lock.
/// </summary>
internal sealed class HeadsView
{
    private readonly ImmutableDictionary<PackageId, ImmutableArray<Head>> heads;

    private HeadsView(ImmutableDictionary<PackageId, ImmutableArray<Head>> heads) => this.heads = heads;

    /// <summary>No Heads at all.</summary>
    public static HeadsView Empty { get; } = new(ImmutableDictionary<PackageId, ImmutableArray<Head>>.Empty);

    /// <summary>The Heads of a Package, ascending by device id; empty when it has none.</summary>
    /// <param name="package">The Package.</param>
    /// <returns>The Heads.</returns>
    public IReadOnlyList<Head> Of(PackageId package) => heads.TryGetValue(package, out ImmutableArray<Head> found) ? found : [];

    /// <summary>A device's Head in a Package, if one is stored.</summary>
    /// <param name="package">The Package.</param>
    /// <param name="device">The writing device.</param>
    /// <param name="head">The Head, or <c>default</c>.</param>
    /// <returns><see langword="true"/> when the device has a Head there.</returns>
    public bool TryGet(PackageId package, DeviceId device, out Head head)
    {
        head = default;
        if (!heads.TryGetValue(package, out ImmutableArray<Head> found))
            return false;
        int at = IndexOf(found, device);
        if (at < 0)
            return false;
        head = found[at];
        return true;
    }

    /// <summary>The view with <paramref name="head"/> as its device's Head in <paramref name="package"/>.</summary>
    /// <param name="package">The Package.</param>
    /// <param name="head">The new Head; the head rule chose it already.</param>
    /// <returns>The new view.</returns>
    public HeadsView With(PackageId package, Head head)
    {
        ImmutableArray<Head> current = heads.TryGetValue(package, out ImmutableArray<Head> found) ? found : [];
        int at = IndexOf(current, head.Device);
        ImmutableArray<Head> next = at >= 0 ? current.SetItem(at, head) : current.Insert(~at, head);
        return new HeadsView(heads.SetItem(package, next));
    }

    /// <summary>The view without the Head that names <paramref name="entry"/>, which was evicted (STRUCTURAL-DEBT.md 18).</summary>
    /// <param name="package">The Package.</param>
    /// <param name="entry">The evicted Entry.</param>
    /// <returns>The new view, or this one when no Head names it.</returns>
    public HeadsView Without(PackageId package, ObjectHash entry)
    {
        if (!heads.TryGetValue(package, out ImmutableArray<Head> current))
            return this;
        int at = -1;
        for (int i = 0; i < current.Length && at < 0; i++)
            at = current[i].Entry == entry ? i : -1;
        if (at < 0)
            return this;
        ImmutableArray<Head> next = current.RemoveAt(at);
        return new HeadsView(next.IsEmpty ? heads.Remove(package) : heads.SetItem(package, next));
    }

    /// <summary>A view of the Heads the index holds.</summary>
    /// <param name="rows">The Heads, per Package.</param>
    /// <returns>The view.</returns>
    public static HeadsView Of(IEnumerable<KeyValuePair<PackageId, Head>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        HeadsView view = Empty;
        foreach (KeyValuePair<PackageId, Head> row in rows)
            view = view.With(row.Key, row.Value);
        return view;
    }

    // Binary search by device id: the index of the device, or the complement of where it would go.
    private static int IndexOf(ImmutableArray<Head> sorted, DeviceId device)
    {
        int low = 0;
        int high = sorted.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            int order = sorted[middle].Device.CompareTo(device);
            if (order == 0)
                return middle;
            if (order < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return ~low;
    }
}
