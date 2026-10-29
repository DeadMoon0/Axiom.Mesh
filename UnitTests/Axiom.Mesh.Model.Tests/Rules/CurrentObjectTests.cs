using System;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §8.7–§8.9 and §9.3: which of several valid objects or Entries is current.</summary>
public sealed class CurrentObjectTests
{
    // §8.7, §8.8, §8.9: "the valid one with the highest seq, ties broken by the lower hash".
    [Fact]
    public void TheHighestSeqIsCurrent_ATieGoesToTheLowerHash()
    {
        Assert.True(CurrentObject.Supersedes(new ObjectSeq(2, RawObjects.Hash(9)), new ObjectSeq(1, RawObjects.Hash(1))));
        Assert.False(CurrentObject.Supersedes(new ObjectSeq(1, RawObjects.Hash(1)), new ObjectSeq(2, RawObjects.Hash(9))));
        Assert.True(CurrentObject.Supersedes(new ObjectSeq(2, RawObjects.Hash(1)), new ObjectSeq(2, RawObjects.Hash(9))));
        Assert.False(CurrentObject.Supersedes(new ObjectSeq(2, RawObjects.Hash(9)), new ObjectSeq(2, RawObjects.Hash(1))));
        Assert.False(CurrentObject.Supersedes(new ObjectSeq(2, RawObjects.Hash(5)), new ObjectSeq(2, RawObjects.Hash(5))));
    }

    // §9.3: "MUST keep, per Package and per writing device, the stored Entry with the highest clock, ties broken by the
    // lower hash."
    [Fact]
    public void AHeadIsTheDevicesEntryWithTheHighestClock_ATieGoesToTheLowerHash()
    {
        DeviceId device = TestKeys.AliceLaptop.PublicKey.AsDeviceId();
        Assert.True(HeadRule.Supersedes(new Head(device, RawObjects.Hash(9), 8), new Head(device, RawObjects.Hash(1), 7)));
        Assert.True(HeadRule.Supersedes(new Head(device, RawObjects.Hash(1), 7), new Head(device, RawObjects.Hash(9), 7)));
        Assert.False(HeadRule.Supersedes(new Head(device, RawObjects.Hash(9), 7), new Head(device, RawObjects.Hash(1), 7)));
        Assert.Throws<ArgumentException>(() => HeadRule.Supersedes(new Head(device, RawObjects.Hash(1), 9), new Head(TestKeys.AlicePhone.PublicKey.AsDeviceId(), RawObjects.Hash(1), 1)));
    }

    // §8.7: "kind MUST NOT change between Manifests of one Package; a Manifest with another kind than the current one is
    // faulty content and ignored."
    [Fact]
    public void AManifestThatChangesItsPackagesKindIsFaulty()
    {
        VerifiedManifest channel = Samples.Channel(seq: 1);
        VerifiedManifest archive = new ManifestDraft { Nonce = Samples.Nonce(1), Kind = PackageKind.Archive, Seq = 2, Name = "Family", Created = 0 }.Sign(TestKeys.Alice);

        Assert.Equal(channel.Content.Package, archive.Content.Package);
        Assert.True(CurrentObject.ChangesKind(archive.Content, channel.Content));
        Assert.False(CurrentObject.ChangesKind(Samples.Channel(seq: 2).Content, channel.Content));
    }
}
