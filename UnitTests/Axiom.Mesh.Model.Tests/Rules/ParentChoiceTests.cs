using System;
using System.Collections.Generic;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §9.3 and LOCAL-SPEC.md §9.4: the clock and parents of an Entry whose writer names none.</summary>
public sealed class ParentChoiceTests
{
    private const ulong Gap = 1UL << 32;

    // §9.3: "A writing device sets clock to one more than the highest clock of any Entry it holds for the Package, or 0
    // when it holds none … MUST name as parents its own current head … a device that holds Entries but has no head of its
    // own names the stored Entry highest in the total order. When the clock so chosen would exceed the own head's clock
    // by more than 2^32 … the device names two parents … A device whose next clock would not be well-formed MUST stop."
    // Every chosen Entry is then checked to be well-formed and faultless by §8.6.
    [Theory]
    // Nothing held: clock 0, no parents.
    [InlineData(null, null, 0UL, "none")]
    // Entries held, no own Head: the highest Entry.
    [InlineData(null, 41UL, 42UL, "highest")]
    // An own Head one below the highest: the own Head alone.
    [InlineData(40UL, 41UL, 42UL, "own")]
    // An own Head exactly 2^32 below the new clock: still the own Head alone.
    [InlineData(1UL, Gap, Gap + 1, "own")]
    // An own Head 2^32 + 1 below the new clock: the own Head and the highest Entry.
    [InlineData(0UL, Gap, Gap + 1, "own+highest")]
    // The highest clock is 2^63 − 1: the next clock would be 2^63, so writing stops.
    [InlineData(5UL, (1UL << 63) - 1, 0UL, "exhausted")]
    public void Decide(ulong? ownClock, ulong? highestClock, ulong expectedClock, string expectedParents)
    {
        DeviceId self = TestKeys.AliceLaptop.PublicKey.AsDeviceId();
        DeviceId other = TestKeys.AlicePhone.PublicKey.AsDeviceId();
        EntryKey? own = ownClock is { } o ? new EntryKey(o, self, RawObjects.Hash(1)) : null;
        EntryKey? highest = highestClock is { } h ? (h == ownClock ? own : new EntryKey(h, other, RawObjects.Hash(2))) : null;

        ParentChoice choice = ParentRule.Choose(own, highest);

        if (expectedParents == "exhausted")
        {
            Assert.IsType<ParentChoice.ClockExhausted>(choice);
            return;
        }

        var write = Assert.IsType<ParentChoice.Write>(choice);
        Assert.Equal(expectedClock, write.Clock);
        EntryParents expected = expectedParents switch
        {
            "none" => EntryParents.None,
            "highest" => EntryParents.Of(highest!.Value.Hash),
            "own" => EntryParents.Of(own!.Value.Hash),
            _ => EntryParents.Of(own!.Value.Hash, highest!.Value.Hash),
        };
        Assert.Equal(expected, write.Parents);

        // The Entry so chosen is well-formed and faultless.
        Assert.True(Lamport.IsWellFormed(write.Clock, write.Parents));
        PackageId package = Samples.Channel().Content.Package;
        var held = new List<EntryFacts>();
        foreach (EntryKey? key in new[] { own, highest })
        {
            if (key is { } k && write.Parents.Contains(k.Hash) && !held.Exists(f => f.Key.Hash == k.Hash))
                held.Add(new EntryFacts(package, EntryKind.Message, k));
        }

        EntryContent entry = Samples.Message(TestKeys.AliceLaptop, package, write.Clock, write.Parents).Content;
        Assert.False(Lamport.TryFindParentFault(entry, held, out ObjectFault fault), fault.Detail);
    }

    // LOCAL-SPEC.md §9.4: a Head that sorts above the highest Entry, or a Head with nothing held, is a caller's mistake.
    [Fact]
    public void InconsistentInputIsRefused()
    {
        DeviceId self = TestKeys.AliceLaptop.PublicKey.AsDeviceId();
        Assert.Throws<ArgumentException>(() => ParentRule.Choose(new EntryKey(1, self, RawObjects.Hash(1)), null));
        Assert.Throws<ArgumentException>(() => ParentRule.Choose(new EntryKey(9, self, RawObjects.Hash(1)), new EntryKey(3, self, RawObjects.Hash(2))));
    }
}
