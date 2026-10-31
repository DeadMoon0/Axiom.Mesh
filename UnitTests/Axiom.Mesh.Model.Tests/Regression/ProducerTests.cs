using System;
using System.Linq;
using Axiom.Mesh.Payloads;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Regression;

/// <summary>What the producers write: the fields they were given, in the canonical order SPEC.md §5.3 demands.</summary>
public sealed class ProducerTests
{
    // Damage: a draft that dropped, reordered or re-derived a field would still produce a verifying object, only one that
    // says something else than the caller asked for.
    [Fact]
    public void EachDraftWritesTheFieldsItWasGiven_InCanonicalOrder()
    {
        VerifiedManifest manifest = new ManifestDraft
        {
            Nonce = Samples.Nonce(9),
            Kind = PackageKind.Archive,
            Seq = 7,
            Name = "Photos",
            Created = 42,
            Retention = Retention.Everything,
            Branches = [new Branch("main", RawObjects.Hash(1)), new Branch("beta", default), new Branch("Zeta", default)],
        }.Sign(TestKeys.Alice);
        Assert.Equal(TestKeys.Alice.PublicKey.AsUserId(), manifest.Content.Owner);
        Assert.Equal(PackageId.Derive(manifest.Content.Owner, Samples.Nonce(9)), manifest.Content.Package);
        Assert.Equal((PackageKind.Archive, 7UL, "Photos", 42UL, Retention.Everything), (manifest.Content.Kind, manifest.Content.Seq, manifest.Content.Name, manifest.Content.Created, manifest.Content.Retention));
        Assert.Equal(new[] { "Zeta", "beta", "main" }, manifest.Content.Branches.Select(b => b.Name));

        VerifiedRoster roster = Samples.Roster(TestKeys.Bob, 3, (TestKeys.BobDesktop, false), (TestKeys.EveDevice, true));
        Assert.True(roster.Content.Devices.Select(d => d.Id).SequenceEqual(roster.Content.Devices.Select(d => d.Id).Order()));
        Assert.True(roster.Content.TryGetDevice(TestKeys.EveDevice.PublicKey.AsDeviceId(), out RosterDevice? eve) && eve.Removed);
        Assert.False(roster.Content.TryGetDevice(TestKeys.AliceLaptop.PublicKey.AsDeviceId(), out _));

        VerifiedGrant grant = Samples.Grant(Samples.Channel(), TestKeys.Bob, GrantRights.Read, seq: 4);
        Assert.Equal((TestKeys.Bob.PublicKey.AsUserId(), GrantRights.Read, 4UL), (grant.Content.Grantee, grant.Content.Rights, grant.Content.Seq));

        VerifiedEntry entry = Samples.Message(TestKeys.AlicePhone, manifest.Content.Package, 12, EntryParents.Of(RawObjects.Hash(8), RawObjects.Hash(3)), "text");
        Assert.Equal(TestKeys.AlicePhone.PublicKey.AsDeviceId(), entry.Content.Device);
        Assert.Equal(new[] { RawObjects.Hash(3), RawObjects.Hash(8) }, entry.Content.Parents);
        Assert.True(EntryPayload.TryParse(entry.Content.Kind, entry.Content.Payload, out EntryPayload? payload));
        Assert.Equal("text", Assert.IsType<MessagePayload>(payload).Text);
    }

    // Damage: a producer that let a caller's mistake through would sign an object every peer drops as malformed, or one
    // that claims rights the signer does not hold.
    [Fact]
    public void ADraftThatBreaksTheSpecificationIsRefusedBeforeAnythingIsSigned()
    {
        VerifiedManifest manifest = Samples.Channel();
        Assert.Throws<ArgumentException>(() => new GrantDraft { Grantee = TestKeys.Bob.PublicKey, Rights = GrantRights.Read, Seq = 1 }.Sign(TestKeys.Eve, manifest));
        Assert.Throws<ArgumentException>(() => new GrantDraft { Grantee = TestKeys.Alice.PublicKey, Rights = GrantRights.Read, Seq = 1 }.Sign(TestKeys.Alice, manifest));
        Assert.Throws<ArgumentException>(() => new GrantDraft { Grantee = TestKeys.Bob.PublicKey, Rights = (GrantRights)3, Seq = 1 }.Sign(TestKeys.Alice, manifest));
        Assert.Throws<ArgumentException>(() => Samples.Channel(name: new string('n', 256)));
        Assert.Throws<ArgumentException>(() => Samples.Channel(name: "a\0b"));
        Assert.Throws<ArgumentException>(() => Samples.Channel(name: "\ud800"));
        Assert.Throws<ArgumentException>(() => Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false), (TestKeys.AliceLaptop, true)));
        Assert.Throws<ArgumentException>(() => ObjectBuilders.Chunk([]));
        Assert.Throws<ArgumentException>(() => ObjectBuilders.Chunk(new byte[Limits.ChunkLength + 1]));
        Assert.Throws<ArgumentException>(() => ObjectBuilders.ChunkList(0, new ObjectHash[Limits.MaxChunkListChunks + 1]));
        Assert.Throws<ArgumentException>(() => EntryReferences.Of(Enumerable.Range(0, Limits.MaxEntryReferences + 1).Select(i => RawObjects.Hash((byte)i))));
        Assert.Throws<ArgumentException>(() => new EntryBody(EntryKind.Message, new byte[Limits.MaxPayloadLength + 1], EntryReferences.None, default));
    }

    // Damage: an ISigner whose key does not match its PublicKey would publish objects that never verify; the producer
    // re-verifies every signature and refuses.
    [Fact]
    public void ASignerWhosePublicKeyIsNotItsKeyIsDetected()
    {
        var mismatched = new MismatchedSigner(TestKeys.AliceLaptop, TestKeys.AlicePhone.PublicKey);
        Assert.Throws<InvalidOperationException>(() => Samples.Message(mismatched, Samples.Channel().Content.Package, 0, EntryParents.None));
    }

    private sealed class MismatchedSigner(ISigner inner, PublicKey claimed) : ISigner
    {
        public PublicKey PublicKey => claimed;

        public Signature64 Sign(SigningDigest digest) => inner.Sign(digest);
    }
}
