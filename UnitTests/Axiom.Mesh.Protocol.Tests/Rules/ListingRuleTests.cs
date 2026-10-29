using System;
using System.Linq;
using Axiom.Mesh.Protocol.Tests.Support;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>The rules for a listing (SPEC.md §7.2) and <c>PackageChanged</c> (§7.3), as the receiver checks them.</summary>
public sealed class ListingRuleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    // §7.2: "Pages are numbered from 0 consecutively; a page out of sequence is a violation."
    [Fact]
    public void PagesAreNumberedFrom0Consecutively_TheLastCompletesTheListing_AnythingElseIsAViolation()
    {
        ListingStep first = ListingRule.Idle.OnPackages(Page(0, last: false), T0);
        Assert.Equal(ListingVerdict.Accepted, first.Verdict);
        ListingStep second = first.Next.OnPackages(Page(1, last: false), T0);
        Assert.Equal(ListingVerdict.Accepted, second.Verdict);
        ListingStep last = second.Next.OnPackages(Page(2, last: true), T0);
        Assert.Equal(ListingVerdict.Completed, last.Verdict);
        Assert.False(last.Next.InProgress);

        Assert.Equal(ListingVerdict.Violation, second.Next.OnPackages(Page(1, last: false), T0).Verdict);
        Assert.Equal(ListingVerdict.Violation, second.Next.OnPackages(Page(3, last: true), T0).Verdict);
        Assert.Equal(ListingVerdict.Violation, ListingRule.Idle.OnPackages(Page(1, last: true), T0).Verdict);
        Assert.Equal(ByeReason.Violation, ListingRule.Idle.OnPackages(Page(1, last: true), T0).Bye!.Reason);

        // A single page 0 that is also the last is a complete listing.
        Assert.Equal(ListingVerdict.Completed, ListingRule.Idle.OnPackages(Page(0, last: true), T0).Verdict);
    }

    // §7.2: "A Packages with page = 0 while a listing is incomplete discards the partial listing and starts over."
    [Fact]
    public void APage0WhileIncompleteStartsOver()
    {
        ListingRule partial = ListingRule.Idle.OnPackages(Page(0, last: false), T0).Next.OnPackages(Page(1, last: false), T0).Next;

        ListingStep restarted = partial.OnPackages(Page(0, last: false), T0.AddSeconds(30));

        Assert.Equal(ListingVerdict.Accepted, restarted.Verdict);
        Assert.Equal(T0.AddSeconds(30 + Limits.ListingSeconds), restarted.Next.Deadline);
        Assert.Equal(ListingVerdict.Accepted, restarted.Next.OnPackages(Page(1, last: false), T0.AddSeconds(31)).Verdict);
    }

    // §7.2: "A listing MUST hold at most 65 536 PackageStates in all and MUST complete within 60 seconds of page 0; more,
    // or later, is a violation."
    [Fact]
    public void AListingOf65536IsAccepted_65537IsAViolation_AndItMustCompleteWithin60SecondsOfPage0()
    {
        int pages = Limits.MaxListingPackages / Limits.MaxPackagesPerPage;
        Packages full = new(0, last: false, [.. Enumerable.Repeat(MessageSamples.State(0, 0), Limits.MaxPackagesPerPage)]);
        ListingRule rule = ListingRule.Idle;
        for (ushort page = 0; page < pages; page++)
            rule = rule.OnPackages(new Packages(page, last: false, full.States), T0).Next;

        Assert.Equal(ListingVerdict.Completed, rule.OnPackages(new Packages((ushort)pages, last: true, []), T0).Verdict);
        Assert.Equal(ListingVerdict.Violation, rule.OnPackages(new Packages((ushort)pages, last: true, [MessageSamples.State(0, 0)]), T0).Verdict);

        ListingRule started = ListingRule.Idle.OnPackages(Page(0, last: false), T0).Next;
        Assert.Equal(T0.AddSeconds(Limits.ListingSeconds), started.Deadline);
        Assert.False(started.IsOverdue(T0.AddSeconds(Limits.ListingSeconds)));
        Assert.True(started.IsOverdue(T0.AddSeconds(Limits.ListingSeconds).AddTicks(1)));
        Assert.Equal(ListingVerdict.Completed, started.OnPackages(Page(1, last: true), T0.AddSeconds(Limits.ListingSeconds)).Verdict);
        Assert.Equal(ListingVerdict.Violation, started.OnPackages(Page(1, last: true), T0.AddSeconds(Limits.ListingSeconds).AddTicks(1)).Verdict);
    }

    // §7.2: "Between the pages of one listing the sender MUST NOT send another Packages or PackageChanged"; §7.3: "A
    // PackageChanged MUST NOT be sent while a listing is incomplete."
    [Fact]
    public void APackageChangedMidListingIsAViolation_BeforeAndAfterItIsAccepted()
    {
        Assert.Equal(ListingVerdict.Accepted, ListingRule.Idle.OnPackageChanged(T0).Verdict);

        ListingRule partial = ListingRule.Idle.OnPackages(Page(0, last: false), T0).Next;
        Assert.Equal(ListingVerdict.Violation, partial.OnPackageChanged(T0.AddSeconds(1)).Verdict);

        ListingRule complete = partial.OnPackages(Page(1, last: true), T0.AddSeconds(2)).Next;
        Assert.Equal(ListingVerdict.Accepted, complete.OnPackageChanged(T0.AddSeconds(3)).Verdict);
    }

    private static Packages Page(ushort page, bool last) => new(page, last, [MessageSamples.State(1, 1)]);
}
