using System;
using System.Linq;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §9.3: the total order of a Package's Entries.</summary>
public sealed class EntryOrderTests
{
    // §9.3: "The total order of Entries in a Package is by (clock, deviceId, hash) ascending. Every node that holds the
    // same set of Entries computes the same order." Ids and hashes compare as unsigned byte strings: 0x80… sorts after
    // 0x7F…, which a signed comparison would get wrong.
    [Fact]
    public void EntriesOrderByClockThenDeviceIdThenHash_IdenticallyOnEveryNode()
    {
        DeviceId low = new(RawObjects.Hash(0x7F).Value);
        DeviceId high = new(RawObjects.Hash(0x80).Value);
        EntryKey[] expected =
        [
            new(0, high, RawObjects.Hash(0xFF)),
            new(1, low, RawObjects.Hash(0xFF)),
            new(1, high, RawObjects.Hash(0x01)),
            new(1, high, RawObjects.Hash(0x80)),
            new(2, low, RawObjects.Hash(0x00)),
            new(ulong.MaxValue >> 1, low, RawObjects.Hash(0x00)),
        ];

        // Two nodes that received the Entries in different orders.
        EntryKey[] nodeA = [.. Enumerable.Reverse(expected)];
        EntryKey[] nodeB = [.. expected.OrderBy(k => k.Hash.ToString(), StringComparer.Ordinal)];
        Array.Sort(nodeA);
        Array.Sort(nodeB);

        Assert.Equal(expected, nodeA);
        Assert.Equal(expected, nodeB);
        Assert.True(expected[1] < expected[2] && expected[2] > expected[1] && expected[3] >= expected[3]);
    }
}
