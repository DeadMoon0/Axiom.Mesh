using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>
/// SPEC.md §8.6 (an Entry stored before its parents is checked when the last of them is stored; one found faulty then is
/// no longer listed as a Head, served or forwarded) and §9.3 (a node keeps, per Package and device, the stored Entry with
/// the highest clock, so a Head that leaves the store hands over to the device's next stored Entry).
/// </summary>
public sealed class LateParentTests
{
    // §8.6: a child whose clock is not above its parent's is faulty; stored first, it stays until the parent arrives, and
    // then leaves the store: not stored, not a Head. The parent becomes the device's Head.
    [Fact]
    public async Task AnEntryStoredBeforeItsParentIsCheckedWhenTheParentArrives_AndAFaultyOneIsNoLongerStoredOrAHead()
    {
        await using TestStore node = await ChannelAsync();
        PackageId channel = Channel.Content.Package;
        VerifiedEntry parent = Samples.Message(TestKeys.AlicePhone, channel, 5, EntryParents.Of(Samples.Message(TestKeys.AlicePhone, channel, 4, EntryParents.Of(Missing)).Hash));
        VerifiedEntry child = Samples.Message(TestKeys.AlicePhone, channel, 5, EntryParents.Of(parent.Hash), "not above its parent");

        Assert.Equal(AdmitOutcome.Stored, (await FromPhoneAsync(node, child)).Outcome);
        Assert.True(await node.ContainsAsync(child.Hash));
        Assert.Equal(child.Hash, HeadOf(node, channel).Entry);

        Assert.Equal(AdmitOutcome.Stored, (await FromPhoneAsync(node, parent)).Outcome);

        Assert.False(await node.ContainsAsync(child.Hash));
        Assert.Equal(parent.Hash, HeadOf(node, channel).Entry);
        Assert.True(await node.ContainsAsync(parent.Hash));
    }

    // §8.6: a child that names two parents is checked once both are stored, not when the first arrives; a faultless one
    // stays the Head.
    [Fact]
    public async Task AChildOfTwoParentsIsCheckedWhenTheLastArrives_AndAFaultlessOneStays()
    {
        await using TestStore node = await ChannelAsync();
        PackageId channel = Channel.Content.Package;
        VerifiedEntry left = Samples.Message(TestKeys.AlicePhone, channel, 0, EntryParents.None, "left");
        VerifiedEntry right = Samples.Message(TestKeys.AliceLaptop, channel, 3, EntryParents.Of(left.Hash), "right");
        VerifiedEntry child = Samples.Message(TestKeys.AlicePhone, channel, 4, Parents(left.Hash, right.Hash), "both");

        await FromPhoneAsync(node, child);
        await FromPhoneAsync(node, right);
        Assert.True(await node.ContainsAsync(child.Hash));
        await FromPhoneAsync(node, left);

        Assert.True(await node.ContainsAsync(child.Hash));
        Assert.Equal(child.Hash, HeadOf(node, channel, TestKeys.AlicePhone).Entry);
    }

    // §8.6: a child that jumps more than 2^32 above its parent is faulty once the parent is stored, whichever came first;
    // stored after its parent it is refused at once, stored before it leaves the store when the parent arrives.
    [Fact]
    public async Task AClockGapAbove2Pow32IsFaultyWhicheverArrivesFirst()
    {
        await using TestStore node = await ChannelAsync();
        PackageId channel = Channel.Content.Package;
        VerifiedEntry parent = Samples.Message(TestKeys.AlicePhone, channel, 0, EntryParents.None, "root");
        VerifiedEntry jumped = Samples.Message(TestKeys.AlicePhone, channel, Limits.MaxClockGap + 1, EntryParents.Of(parent.Hash), "jumped");

        await FromPhoneAsync(node, jumped);
        await FromPhoneAsync(node, parent);
        Assert.False(await node.ContainsAsync(jumped.Hash));

        Assert.Equal(AdmitOutcome.Faulty, (await FromPhoneAsync(node, jumped)).Outcome);
        Assert.Equal(parent.Hash, HeadOf(node, channel).Entry);
    }

    // §9.3: evicting a device's Head (an unfollowed Package's Entries are evictable) elects the device's next stored Entry,
    // so the Heads keep naming the highest stored clock per device.
    [Fact]
    public async Task EvictingADevicesHeadMakesItsNextStoredEntryTheHead()
    {
        await using TestStore node = await ChannelAsync();
        PackageId channel = Channel.Content.Package;
        VerifiedEntry first = Samples.Message(TestKeys.AlicePhone, channel, 0, EntryParents.None, "first");
        VerifiedEntry second = Samples.Message(TestKeys.AlicePhone, channel, 1, EntryParents.Of(first.Hash), "second");
        await FromPhoneAsync(node, first);
        await FromPhoneAsync(node, second);
        Assert.Equal(second.Hash, HeadOf(node, channel).Entry);

        await node.UnfollowAsync(channel);
        Assert.Equal(EvictionOutcome.Evicted, await node.Store.TryEvictAsync(second.Hash, default));

        Assert.Equal(first.Hash, HeadOf(node, channel).Entry);
        Assert.Equal(EvictionOutcome.Evicted, await node.Store.TryEvictAsync(first.Hash, default));
        Assert.False(node.Writer.Heads.TryGet(channel, TestKeys.AlicePhone.PublicKey.AsDeviceId(), out _));
    }

    private static VerifiedManifest Channel { get; } = Samples.Channel(TestKeys.Alice);

    private static ObjectHash Missing { get; } = ObjectHash.Of("never stored"u8);

    // Bob's desktop follows a Channel of Alice's, whose phone and laptop write to it.
    private static async Task<TestStore> ChannelAsync()
    {
        TestStore node = await TestStore.StartAsync();
        await node.IdentifyAsync(TestKeys.Bob, TestKeys.BobDesktop);
        await node.PutAsync(Samples.Roster(TestKeys.Bob, 1, (TestKeys.BobDesktop, false)));
        await node.PairAsync(TestKeys.Alice);
        await node.PutAsync(Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, false)));
        await node.FollowAsync(Channel.Content.Package);
        await node.PutAsync(Channel);
        return node;
    }

    private static Task<AdmitResult> FromPhoneAsync(TestStore node, VerifiedEntry entry) =>
        node.PutFromPeerAsync(Samples.Admit(entry, node.Trust), TestKeys.AlicePhone);

    private static Head HeadOf(TestStore node, PackageId package) => HeadOf(node, package, TestKeys.AlicePhone);

    private static Head HeadOf(TestStore node, PackageId package, ISigner device)
    {
        Assert.True(node.Writer.Heads.TryGet(package, device.PublicKey.AsDeviceId(), out Head head));
        return head;
    }

    private static EntryParents Parents(ObjectHash one, ObjectHash other) =>
        one < other ? EntryParents.Of(one, other) : EntryParents.Of(other, one);
}
