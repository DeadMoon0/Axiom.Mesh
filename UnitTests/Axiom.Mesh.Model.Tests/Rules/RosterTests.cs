using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §8.8: Rosters, removal, and a device id held by two users.</summary>
public sealed class RosterTests
{
    private static readonly HashSet<DeviceId> None = [];

    // §8.8: "Removal is permanent: a device that was removed in any Roster this node verified is treated as removed in
    // every later Roster of that user, whatever its flag … A later Roster that omits a device is accepted and treated as
    // if the device were marked removed … when a later verified Roster conflicts with an earlier one, the later Roster is
    // held but its conflicting device is treated as removed."
    [Fact]
    public void RemovalIsPermanent_AnOmittedDeviceCountsAsRemoved_ADeviceIdInTwoUsersRostersIsRemovedInTheLater()
    {
        DeviceId laptop = TestKeys.AliceLaptop.PublicKey.AsDeviceId();
        DeviceId phone = TestKeys.AlicePhone.PublicKey.AsDeviceId();

        VerifiedRoster first = Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, true));
        RosterApplication applied = RosterRules.Apply(null, first, None, None);
        Assert.Equal(DeviceRemoval.Flagged, Removal(applied, phone));
        Assert.Equal(DeviceRemoval.None, Removal(applied, laptop));

        // The phone is listed again without the flag: still removed.
        VerifiedRoster second = Samples.Roster(TestKeys.Alice, 2, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, false));
        applied = RosterRules.Apply(first, second, Set(applied.RemovedEver), None);
        Assert.Equal(DeviceRemoval.RemovedBefore, Removal(applied, phone));

        // The laptop is omitted, then listed again: removed from the omission on.
        VerifiedRoster third = Samples.Roster(TestKeys.Alice, 3, (TestKeys.AlicePhone, false));
        applied = RosterRules.Apply(second, third, Set(applied.RemovedEver), None);
        Assert.Equal(new[] { laptop }, applied.Omitted);
        VerifiedRoster fourth = Samples.Roster(TestKeys.Alice, 4, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, false));
        applied = RosterRules.Apply(third, fourth, Set(applied.RemovedEver), None);
        Assert.Equal(DeviceRemoval.RemovedBefore, Removal(applied, laptop));

        // Bob's later Roster claims a device Alice's current Roster holds: removed in Bob's.
        VerifiedRoster bobs = Samples.Roster(TestKeys.Bob, 1, (TestKeys.BobDesktop, false), (TestKeys.AlicePhone, false));
        RosterApplication bob = RosterRules.Apply(null, bobs, None, new HashSet<DeviceId> { phone });
        Assert.Equal(DeviceRemoval.HeldByAnotherUser, Removal(bob, phone));
        Assert.Equal(DeviceRemoval.None, Removal(bob, TestKeys.BobDesktop.PublicKey.AsDeviceId()));
    }

    // §8.8: "the valid one with the highest seq, ties broken by the lower hash": an older Roster never replaces a newer.
    [Fact]
    public void AnOlderRosterDoesNotReplaceANewerOne()
    {
        VerifiedRoster newer = Samples.Roster(TestKeys.Alice, 5, (TestKeys.AliceLaptop, false));
        VerifiedRoster older = Samples.Roster(TestKeys.Alice, 4, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, false));
        Assert.Throws<ArgumentException>(() => RosterRules.Apply(newer, older, None, None));
        Assert.Throws<ArgumentException>(() => RosterRules.Apply(Samples.Roster(TestKeys.Bob, 1), newer, None, None));
    }

    // §2.3: "a roster device whose id equals a known user id … is treated as malformed where it appears": a Roster that
    // lists its own user key as a device is malformed, and cannot be drafted.
    [Fact]
    public void ARosterThatListsItsOwnUserKeyAsADeviceIsMalformed()
    {
        Assert.Throws<ArgumentException>(() => Samples.Roster(TestKeys.Alice, 1, (TestKeys.Alice, false)));

        byte[] bytes = RawObjects.FakeSigned(ObjectTag.Roster, output =>
        {
            var writer = new Axiom.ProtoStream.Codecs.WireWriter(output);
            TestKeys.Alice.PublicKey.AsUserId().Write(ref writer);
            TestKeys.Alice.PublicKey.Write(ref writer);
            writer.UInt64BigEndian(1);
            writer.UInt16BigEndian(1);
            TestKeys.Alice.PublicKey.AsDeviceId().Write(ref writer);
            TestKeys.Alice.PublicKey.Write(ref writer);
            writer.UInt16BigEndian(0);
            writer.UInt64BigEndian(0);
            writer.UInt8(0);
        });
        Assert.Contains("never both", Samples.Fault(bytes).Detail, StringComparison.Ordinal);
    }

    private static DeviceRemoval Removal(RosterApplication applied, DeviceId device) =>
        applied.Devices.Single(d => d.Device.Id == device).Removal;

    private static HashSet<DeviceId> Set(IEnumerable<DeviceId> devices) => [.. devices];
}
