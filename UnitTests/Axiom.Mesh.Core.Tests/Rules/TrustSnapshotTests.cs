using System;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Rules;

/// <summary>SPEC.md §8.7–§8.9 (current objects, permanent removal) and §2.3, §4.3 (pinning a TLS key to a Roster), as the node's trust holds them.</summary>
public sealed class TrustSnapshotTests
{
    // SPEC.md §8.7, §8.8, §8.9: of several valid Manifests (per Package), Rosters (per user) and Grants (per Package and
    // grantee) the one with the highest seq is current, ties broken by the lower hash; an older one never rolls it back,
    // and a Manifest of another kind is faulty and ignored. The node holds the same after a restart.
    [Fact]
    public async Task TheCurrentManifestRosterOrGrantIsTheHighestSeqTiesBrokenByTheLowerHash()
    {
        TestStore node = await TestStore.StartAsync();
        await node.IdentifyAsync(TestKeys.Alice, TestKeys.AliceLaptop);
        VerifiedManifest first = Samples.Channel(TestKeys.Alice, seq: 1);
        PackageId id = first.Content.Package;
        await node.FollowAsync(id);
        VerifiedManifest second = Samples.Channel(TestKeys.Alice, seq: 2);
        (VerifiedManifest lower, VerifiedManifest higher) = Order(
            new ManifestDraft { Nonce = Samples.Nonce(1), Kind = PackageKind.Channel, Seq = 3, Name = "Tie A", Created = Samples.Created }.Sign(TestKeys.Alice),
            new ManifestDraft { Nonce = Samples.Nonce(1), Kind = PackageKind.Channel, Seq = 3, Name = "Tie B", Created = Samples.Created }.Sign(TestKeys.Alice));

        Assert.Equal(AdmitOutcome.Stored, (await node.PutAsync(second)).Outcome);
        Assert.Equal(AdmitOutcome.Superseded, (await node.PutAsync(first)).Outcome);
        Assert.Equal(AdmitOutcome.Stored, (await node.PutAsync(higher)).Outcome);
        Assert.Equal(AdmitOutcome.Stored, (await node.PutAsync(lower)).Outcome);
        VerifiedManifest otherKind = new ManifestDraft { Nonce = Samples.Nonce(1), Kind = PackageKind.Archive, Seq = 9, Name = "Kind", Created = Samples.Created }.Sign(TestKeys.Alice);
        Assert.Equal(AdmitOutcome.KindChanged, (await node.PutAsync(otherKind)).Outcome);

        VerifiedRoster roster2 = Samples.Roster(TestKeys.Alice, 2, (TestKeys.AliceLaptop, false));
        await node.PutAsync(roster2);
        Assert.Equal(AdmitOutcome.Superseded, (await node.PutAsync(Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, false)))).Outcome);

        VerifiedManifest current = lower;
        (VerifiedGrant lowGrant, VerifiedGrant highGrant) = Order(
            Samples.Grant(TestKeys.Alice, current, TestKeys.Bob, GrantRights.Read, seq: 4),
            Samples.Grant(TestKeys.Alice, current, TestKeys.Bob, GrantRights.Write, seq: 4));
        Assert.Equal(AdmitOutcome.Stored, (await node.PutAsync(highGrant)).Outcome);
        Assert.Equal(AdmitOutcome.Stored, (await node.PutAsync(lowGrant)).Outcome);
        Assert.Equal(AdmitOutcome.Superseded, (await node.PutAsync(Samples.Grant(TestKeys.Alice, current, TestKeys.Bob, GrantRights.Write, seq: 3))).Outcome);

        for (int run = 0; run < 2; run++)
        {
            TrustSnapshot trust = node.Trust;
            Assert.True(trust.TryGetManifest(id, out VerifiedManifest? manifest));
            Assert.Equal(lower.Hash, manifest.Hash);
            Assert.True(trust.TryGetUser(TestKeys.Alice.PublicKey.AsUserId(), out UserTrust? alice));
            Assert.Equal(roster2.Hash, alice.Roster!.Hash);
            Assert.True(trust.TryGetGrant(id, TestKeys.Bob.PublicKey.AsUserId(), out VerifiedGrant? grant));
            Assert.Equal(lowGrant.Hash, grant.Hash);
            node = await node.RestartAsync();
        }

        await node.DisposeAsync();
    }

    // SPEC.md §8.8: removal is permanent, whatever a later Roster's flag; a device a later Roster omits counts as
    // removed; a device id that two users' Rosters list is removed in the later one. The record survives a restart.
    [Fact]
    public async Task RemovalIsPermanent_AnOmittedDeviceCountsAsRemoved_ADeviceIdInTwoUsersRostersIsRemovedInTheLater()
    {
        TestStore node = await TestStore.StartAsync();
        await node.IdentifyAsync(TestKeys.Alice, TestKeys.AliceLaptop);
        await node.PairAsync(TestKeys.Bob);
        await node.PairAsync(TestKeys.Eve);
        DeviceId laptop = TestKeys.AliceLaptop.PublicKey.AsDeviceId();
        DeviceId phone = TestKeys.AlicePhone.PublicKey.AsDeviceId();
        DeviceId desktop = TestKeys.BobDesktop.PublicKey.AsDeviceId();

        await node.PutAsync(Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, true)));
        Assert.True(node.Trust.IsRemoved(phone));
        await node.PutAsync(Samples.Roster(TestKeys.Alice, 2, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, false)));
        Assert.True(node.Trust.IsRemoved(phone));
        await node.PutAsync(Samples.Roster(TestKeys.Alice, 3, (TestKeys.AlicePhone, false)));
        Assert.False(node.Trust.TryGetDevice(laptop, out _)); // omitted: no current Roster lists it
        await node.PutAsync(Samples.Roster(TestKeys.Bob, 1, (TestKeys.BobDesktop, false)));
        await node.PutAsync(Samples.Roster(TestKeys.Eve, 1, (TestKeys.BobDesktop, false), (TestKeys.EveDevice, false)));

        node = await node.RestartAsync();
        await node.PutAsync(Samples.Roster(TestKeys.Alice, 4, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, false)));

        TrustSnapshot trust = node.Trust;
        Assert.True(trust.IsRemoved(laptop));
        Assert.True(trust.IsRemoved(phone));
        Assert.True(trust.TryGetDevice(desktop, out TrustedDevice bobs));
        Assert.Equal(TestKeys.Bob.PublicKey.AsUserId(), bobs.User);
        Assert.False(bobs.IsRemoved);
        Assert.True(trust.TryGetUser(TestKeys.Eve.PublicKey.AsUserId(), out UserTrust? eve));
        Assert.Contains(eve.Devices, d => d.Device.Id == desktop && d.Removal == DeviceRemoval.HeldByAnotherUser);
        await node.DisposeAsync();
    }

    // SPEC.md §2.3, §4.3: a TLS peer is pinned by H(its key) to a not-removed device of a known user's current Roster;
    // anything else resolves to nothing. The key instance stays the same across snapshots, so its verifier is made once.
    [Fact]
    public async Task ATlsKeyResolvesToItsDeviceAndUserOnlyWhileItIsANotRemovedDeviceOfAKnownUser()
    {
        await using TestStore node = await TestStore.StartAsync();
        await node.IdentifyAsync(TestKeys.Alice, TestKeys.AliceLaptop);
        await node.PutAsync(Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, false)));
        Assert.True(node.Trust.TryGetDevice(TestKeys.AliceLaptop.PublicKey.AsDeviceId(), out TrustedDevice before));

        await node.PutAsync(Samples.Roster(TestKeys.Alice, 2, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, true)));
        TrustSnapshot trust = node.Trust;

        Assert.True(trust.TryResolveDevice(TestKeys.AliceLaptop.PublicKey.Encoded, out DeviceId device, out UserId user));
        Assert.Equal(TestKeys.AliceLaptop.PublicKey.AsDeviceId(), device);
        Assert.Equal(TestKeys.Alice.PublicKey.AsUserId(), user);
        Assert.False(trust.TryResolveDevice(TestKeys.AlicePhone.PublicKey.Encoded, out _, out _));
        Assert.False(trust.TryResolveDevice(TestKeys.EveDevice.PublicKey.Encoded, out _, out _));
        Assert.False(trust.TryResolveDevice(TestKeys.AliceLaptop.PublicKey.Encoded[..64], out _, out _));
        Assert.True(trust.TryGetDevice(device, out TrustedDevice after));
        Assert.Same(before.Key, after.Key);
    }

    private static (T Lower, T Higher) Order<T>(T one, T other)
        where T : StorableObject => one.Hash < other.Hash ? (one, other) : (other, one);
}
