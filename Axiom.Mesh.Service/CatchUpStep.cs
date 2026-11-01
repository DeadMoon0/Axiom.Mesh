using System;

namespace Axiom.Mesh.Service;

/// <summary>
/// The next step of a catch-up (LOCAL-SPEC.md §7.2, §9.3): while more than 256 stored Entries may lie between where the
/// service stands and the Host's newest, it pages with <c>ListStored</c>; once the rest fits the replay of one
/// <c>Subscribe</c>, it subscribes to topic entries from there and continues live without a gap or a repeat.
/// </summary>
public sealed record CatchUpStep
{
    /// <summary>The most a <c>Subscribe</c> replays before it fails <c>GapTooLarge</c> (§7.2, §12).</summary>
    public const ulong MaxReplay = 256;

    private CatchUpStep(CatchUpAction action, ulong fromSeq)
    {
        Action = action;
        FromSeq = fromSeq;
    }

    /// <summary>Page or subscribe.</summary>
    public CatchUpAction Action { get; }

    /// <summary>The store sequence to page or subscribe from; never 0.</summary>
    public ulong FromSeq { get; }

    /// <summary>Decides the next step.</summary>
    /// <param name="fromSeq">The first store sequence the service still needs; 0 reads as 1.</param>
    /// <param name="hostSeq">The newest store sequence the Host reported (<c>HelloAck</c>, <c>RegisterKind</c>, the last page).</param>
    /// <returns>Page from <paramref name="fromSeq"/> while more than 256 may be missing, else subscribe from it.</returns>
    public static CatchUpStep Next(ulong fromSeq, ulong hostSeq)
    {
        ulong from = Math.Max(fromSeq, 1);
        ulong missing = hostSeq >= from ? hostSeq - from + 1 : 0;
        return new CatchUpStep(missing > MaxReplay ? CatchUpAction.Page : CatchUpAction.Subscribe, from);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Action} from {FromSeq}";
}
