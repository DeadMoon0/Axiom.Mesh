using System;

namespace Axiom.Mesh.Discovery.Tests.Rules;

/// <summary>SPEC.md §3.2: "A node MUST NOT send more than one announcement per second per interface."</summary>
public sealed class AnnounceGateTests
{
    [Fact]
    public void AtMostOneAnnouncementPerSecondPerInterface()
    {
        var gate = new AnnounceGate();
        TimeSpan start = TimeSpan.FromSeconds(100);

        Assert.True(gate.TryPass(start, interfaceIndex: 3));
        Assert.False(gate.TryPass(start, interfaceIndex: 3));
        Assert.False(gate.TryPass(start + TimeSpan.FromMilliseconds(999), interfaceIndex: 3));
        Assert.True(gate.TryPass(start + TimeSpan.FromSeconds(1), interfaceIndex: 3));

        // Another interface has its own second.
        Assert.True(gate.TryPass(start + TimeSpan.FromMilliseconds(1_500), interfaceIndex: 4));
        Assert.False(gate.TryPass(start + TimeSpan.FromMilliseconds(1_500), interfaceIndex: 3));
    }

    // A refused announcement was not sent, so it does not start a new second: a flood of queries every 100 ms is
    // answered exactly once per second.
    [Fact]
    public void AFloodOfQueriesIsAnsweredOncePerSecond()
    {
        var gate = new AnnounceGate();
        int passed = 0;
        for (int tenth = 0; tenth < 100; tenth++)
        {
            if (gate.TryPass(TimeSpan.FromMilliseconds(tenth * 100), interfaceIndex: 1))
                passed++;
        }

        Assert.Equal(10, passed);
    }

    // Two threads racing: a timestamp taken before the last pass is less than a second after it, and refused.
    [Fact]
    public void ATimestampOlderThanTheLastPassIsRefused()
    {
        var gate = new AnnounceGate();

        Assert.True(gate.TryPass(TimeSpan.FromSeconds(10), interfaceIndex: 1));
        Assert.False(gate.TryPass(TimeSpan.FromSeconds(9.5), interfaceIndex: 1));
    }
}
