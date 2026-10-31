using System;
using System.Text;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §8.6 and §9.3: an Entry's clock and parents.</summary>
public sealed class ClockTests
{
    private static readonly PackageId Package = Samples.Channel().Content.Package;

    // §8.6: "clock MUST be below 2^63; parents MAY be empty only when clock is 0; otherwise malformed. Once its parents
    // are held, an Entry is faulty content … if clock exceeds the greatest parent clock by more than 2^32."
    [Fact]
    public void AClockOf2Pow63IsMalformed_EmptyParentsOnlyAtClock0_AGapAbove2Pow32IsFaulty()
    {
        ObjectHash parent = RawObjects.Hash(5);
        Assert.Equal(ObjectFaultKind.Malformed, Samples.Fault(RawEntry(1UL << 63, [parent])).Kind);
        Samples.Parse<UnverifiedEntry>(RawEntry((1UL << 63) - 1, [parent]));

        Assert.Equal(ObjectFaultKind.Malformed, Samples.Fault(RawEntry(1, [])).Kind);
        Samples.Parse<UnverifiedEntry>(RawEntry(0, []));

        EntryContent atGap = Samples.Parse<UnverifiedEntry>(RawEntry(100 + (1UL << 32), [parent])).Content;
        EntryContent pastGap = Samples.Parse<UnverifiedEntry>(RawEntry(101 + (1UL << 32), [parent])).Content;
        EntryFacts held = new(Package, default, new EntryKey(100, TestKeys.AlicePhone.PublicKey.AsDeviceId(), parent));

        Assert.False(Lamport.TryFindParentFault(atGap, [held], out _));
        Assert.True(Lamport.TryFindParentFault(pastGap, [held], out ObjectFault fault));
        Assert.Equal(ObjectFaultKind.Faulty, fault.Kind);
    }

    // §8.6: faulty "if any parent belongs to another packageId, if clock is not greater than every parent's clock"; the
    // greatest of two parents bounds the gap.
    [Fact]
    public void AParentOfAnotherPackageOrWithAnEqualClockMakesTheEntryFaulty()
    {
        ObjectHash low = RawObjects.Hash(1);
        ObjectHash high = RawObjects.Hash(2);
        EntryContent entry = Samples.Parse<UnverifiedEntry>(RawEntry(10, [low, high])).Content;
        DeviceId device = TestKeys.AlicePhone.PublicKey.AsDeviceId();

        Assert.False(Lamport.TryFindParentFault(entry, [new(Package, default, new(9, device, high)), new(Package, default, new(3, device, low))], out _));
        Assert.True(Lamport.TryFindParentFault(entry, [new(Package, default, new(10, device, high)), new(Package, default, new(3, device, low))], out _));
        Assert.True(Lamport.TryFindParentFault(entry, [new(new PackageId(RawObjects.Hash(7).Value), default, new(9, device, high)), new(Package, default, new(3, device, low))], out _));
    }

    // §8.6: "parents … ascending as byte strings"; a list out of order, or one parent named twice, is malformed.
    [Fact]
    public void ParentsOutOfOrderOrRepeatedAreMalformed()
    {
        Assert.Equal(ObjectFaultKind.Malformed, Samples.Fault(RawEntry(5, [RawObjects.Hash(2), RawObjects.Hash(1)])).Kind);
        Assert.Equal(ObjectFaultKind.Malformed, Samples.Fault(RawEntry(5, [RawObjects.Hash(1), RawObjects.Hash(1)])).Kind);
        Assert.Throws<ArgumentException>(() => EntryParents.Of(RawObjects.Hash(1), RawObjects.Hash(1)));
    }

    // §9.3: a device whose next clock would not be well-formed must stop writing; the producer refuses it.
    [Fact]
    public void AnEntryThatIsNotWellFormedCannotBeSigned()
    {
        Assert.Throws<ArgumentException>(() => Samples.Message(TestKeys.AliceLaptop, Package, 1UL << 63, EntryParents.Of(RawObjects.Hash(1))));
        Assert.Throws<ArgumentException>(() => Samples.Message(TestKeys.AliceLaptop, Package, 1, EntryParents.None));
    }

    private static byte[] RawEntry(ulong clock, ObjectHash[] parents) =>
        RawObjects.Signed(
            ObjectTag.Entry,
            SigningDomain.Entry,
            TestKeys.AliceLaptop,
            RawObjects.EntryBody(Package, TestKeys.AliceLaptop.PublicKey.AsDeviceId(), clock, parents, 7, Encoding.UTF8.GetBytes("opaque")));
}
