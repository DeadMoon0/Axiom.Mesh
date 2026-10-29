using System;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// The receiving side of the listing rules (SPEC.md §7.2), one per session: pages numbered from 0 consecutively, at most
/// 65 536 <c>PackageState</c>s in all, complete within 60 seconds of page 0, and no <c>PackageChanged</c> between the
/// pages. An immutable value: every message gives a <see cref="ListingStep"/> holding the next state.
/// </summary>
/// <remarks>
/// A page 0 while a listing is incomplete discards it and starts over, with a new deadline, unless the incomplete one was
/// already overdue. The rule takes timestamps and reads no clock; arm a timer for <see cref="Deadline"/> and ask
/// <see cref="IsOverdue"/> so that a listing that stops arriving is a violation too.
/// </remarks>
public readonly struct ListingRule
{
    private readonly DateTimeOffset started;
    private readonly int received;
    private readonly ushort nextPage;

    private ListingRule(DateTimeOffset started, int received, ushort nextPage)
    {
        this.started = started;
        this.received = received;
        this.nextPage = nextPage;
        InProgress = true;
    }

    /// <summary>No listing in progress: the state of a new session, and after every completed listing.</summary>
    public static ListingRule Idle => default;

    /// <summary>Whether a listing has started and not completed.</summary>
    public bool InProgress { get; }

    /// <summary>When the listing in progress must have completed (§7.2), or <see langword="null"/> when none is.</summary>
    public DateTimeOffset? Deadline => InProgress ? started + Window : null;

    /// <summary>Whether a listing is in progress past its 60 seconds: a violation.</summary>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when the peer is late.</returns>
    public bool IsOverdue(DateTimeOffset now) => InProgress && now - started > Window;

    /// <summary>Checks one page of a listing.</summary>
    /// <param name="page">The page received.</param>
    /// <param name="now">When it was received.</param>
    /// <returns>Accepted, Completed (replace the view) or Violation.</returns>
    public ListingStep OnPackages(Packages page, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (IsOverdue(now))
            return Violation("the listing did not complete within 60 seconds of page 0");

        ListingRule current = this;
        if (page.Page == 0)
            current = new ListingRule(now, 0, 0);
        else if (!InProgress)
            return Violation($"page {page.Page} arrived without a page 0");
        else if (page.Page != nextPage)
            return Violation($"page {page.Page} arrived where page {nextPage} was due");

        int total = current.received + page.States.Count;
        if (total > Limits.MaxListingPackages)
            return Violation($"the listing holds more than {Limits.MaxListingPackages} packages");
        if (page.Last)
            return new ListingStep(ListingVerdict.Completed, Idle, string.Empty);
        if (page.Page == ushort.MaxValue)
            return Violation("the listing has more pages than a page number counts");

        return new ListingStep(ListingVerdict.Accepted, new ListingRule(current.started, total, (ushort)(page.Page + 1)), string.Empty);
    }

    /// <summary>Checks a <c>PackageChanged</c>: never between the pages of a listing (§7.2, §7.3).</summary>
    /// <param name="now">When it was received.</param>
    /// <returns>Accepted or Violation.</returns>
    public ListingStep OnPackageChanged(DateTimeOffset now)
    {
        if (IsOverdue(now))
            return Violation("the listing did not complete within 60 seconds of page 0");
        return InProgress
            ? Violation("a PackageChanged arrived between the pages of a listing")
            : new ListingStep(ListingVerdict.Accepted, this, string.Empty);
    }

    /// <inheritdoc />
    public override string ToString() => InProgress ? $"listing: {received} packages, page {nextPage} due by {Deadline:O}" : "idle";

    private static TimeSpan Window => TimeSpan.FromSeconds(Limits.ListingSeconds);

    private ListingStep Violation(string detail) => new(ListingVerdict.Violation, this, detail);
}
