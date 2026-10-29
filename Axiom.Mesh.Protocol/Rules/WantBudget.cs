using System;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// The bound on wants outstanding between two peers (SPEC.md §7.5): at most 64 hashes wanted and not yet answered with
/// <c>Object</c> or <c>NotFound</c> or cancelled, deferred ones included (§7.6). The receiving side counts a peer's
/// wants with it, and a <c>Want</c> that would exceed it is a violation; the sending side asks <see cref="Remaining"/>
/// before it wants more. An immutable value.
/// </summary>
public readonly struct WantBudget
{
    private WantBudget(int outstanding) => Outstanding = outstanding;

    /// <summary>Nothing outstanding: the state of a new session.</summary>
    public static WantBudget Empty => default;

    /// <summary>Hashes wanted and not yet answered or cancelled.</summary>
    public int Outstanding { get; }

    /// <summary>How many more hashes may be wanted now.</summary>
    public int Remaining => Limits.MaxOutstandingWants - Outstanding;

    /// <summary>Counts a <c>Want</c> of <paramref name="hashes"/> hashes.</summary>
    /// <param name="hashes">The number of hashes the <c>Want</c> carries.</param>
    /// <param name="next">The budget with them outstanding; unchanged when refused.</param>
    /// <returns><see langword="false"/> when more than 64 would be outstanding: a violation on the receiving side.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashes"/> is negative.</exception>
    public bool TryWant(int hashes, out WantBudget next)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hashes);
        bool fits = hashes <= Remaining;
        next = fits ? new WantBudget(Outstanding + hashes) : this;
        return fits;
    }

    /// <summary>Counts hashes answered (<c>Object</c> or <c>NotFound</c>) or cancelled.</summary>
    /// <param name="hashes">How many outstanding hashes were settled.</param>
    /// <returns>The budget without them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Negative, or more than are outstanding: the caller lost count.</exception>
    public WantBudget Settle(int hashes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hashes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hashes, Outstanding);
        return new WantBudget(Outstanding - hashes);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Outstanding} of {Limits.MaxOutstandingWants} wants outstanding";
}
