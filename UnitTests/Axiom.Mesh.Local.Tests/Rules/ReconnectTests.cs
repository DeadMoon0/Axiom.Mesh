using System;

namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>Reconnecting (LOCAL-SPEC.md §2.3): 1 second after the first failure, doubling up to 30 seconds.</summary>
public sealed class ReconnectTests
{
    [Theory]
    [InlineData(1, 1)] // the first failure waits one second
    [InlineData(2, 2)] // then it doubles
    [InlineData(3, 4)] // and doubles
    [InlineData(4, 8)] // and doubles
    [InlineData(5, 16)] // and doubles
    [InlineData(6, 30)] // 32 would pass the cap of 30
    [InlineData(7, 30)] // and stays there
    [InlineData(64, 30)] // where a shift would overflow
    [InlineData(int.MaxValue, 30)] // however long the Host is gone
    public void TheReconnectDelayIs1SecondDoublingTo30(int attempt, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), Reconnect.Delay(attempt));
    }

    [Fact]
    public void ThereIsNoAttemptZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Reconnect.Delay(0));
    }
}
