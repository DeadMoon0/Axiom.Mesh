using System;

namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>SYNC-SERVICE.md §3: a binding waiting for objects tries again after 2 seconds, doubling, at most every minute.</summary>
public sealed class WaitRetryTests
{
    [Theory]
    [InlineData(1, 2)] // the first wait
    [InlineData(2, 4)] // doubled
    [InlineData(5, 32)] // doubled with every wait in a row
    [InlineData(6, 60)] // never longer than a minute
    [InlineData(1000, 60)] // however long no peer supplies them
    [InlineData(0, 2)] // a count below one reads as the first
    public void ABindingWaitingForObjectsTriesAgainAfterTwoSecondsDoubling_AtMostEveryMinute(int waitsInARow, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), WaitRetry.After(waitsInARow));
    }
}
