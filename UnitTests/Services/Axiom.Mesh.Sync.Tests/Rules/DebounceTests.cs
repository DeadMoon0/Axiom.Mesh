using System;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>SYNC-SERVICE.md §3: a run starts once the directory was quiet for 2 seconds, and at most 30 seconds after a burst began.</summary>
public sealed class DebounceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 2)] // one change: two quiet seconds later
    [InlineData(10, 12)] // changes for ten seconds: two seconds after the last
    [InlineData(29, 30)] // a busy directory: never later than thirty seconds after the first
    [InlineData(100, 30)] // however long it stays busy
    public void ARunStartsAfterTwoQuietSeconds_NoLaterThanThirtyAfterTheFirstChange(int lastChange, int due)
    {
        Assert.Equal(Start.AddSeconds(due), Debounce.Next(Start, Start.AddSeconds(lastChange)));
    }
}
