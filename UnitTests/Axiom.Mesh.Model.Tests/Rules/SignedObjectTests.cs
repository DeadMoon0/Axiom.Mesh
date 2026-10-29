using System;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §2.2, §8.1, §8.6–§8.9: every signed object verifies over the bytes received, against the key its section names.</summary>
public sealed class SignedObjectTests
{
    // §15: "every signed object verified, and found invalid after one bit of its body is flipped (which also renames it)".
    // The bits flipped sit in the clock (Entry) or the seq (Manifest, Roster, Grant), so the object stays well-formed and
    // only the signature can refuse it.
    [Fact]
    public void OneFlippedBitInTheBodyMakesAnEntryManifestRosterOrGrantInvalidAndRenamesIt()
    {
        VerifiedManifest manifest = Samples.Channel(seq: 5);
        VerifiedRoster roster = Samples.Roster(TestKeys.Alice, 5, (TestKeys.AliceLaptop, false));
        VerifiedGrant grant = Samples.Grant(manifest, TestKeys.Bob, GrantRights.Read, seq: 5);
        VerifiedEntry entry = Samples.Message(TestKeys.AliceLaptop, manifest.Content.Package, 5, EntryParents.Of(RawObjects.Hash(9)));

        // Offsets: tag and version, then the fields of §8.6–§8.9 up to the last byte of the clock or seq.
        byte[] entryBytes = Flip(entry.Bytes, 2 + 32 + 32 + 7);
        byte[] manifestBytes = Flip(manifest.Bytes, 2 + 32 + 32 + 65 + 32 + 2 + 7);
        byte[] rosterBytes = Flip(roster.Bytes, 2 + 32 + 65 + 7);
        byte[] grantBytes = Flip(grant.Bytes, 2 + 32 + 32 + 65 + 1 + 7);

        UnverifiedEntry flippedEntry = Samples.Parse<UnverifiedEntry>(entryBytes);
        Assert.Equal(4UL, flippedEntry.Content.Clock);
        Assert.NotEqual(entry.Hash, flippedEntry.Hash);
        Assert.False(VerifiedEntry.TryVerify(flippedEntry, TestKeys.AliceLaptop.PublicKey, out _));

        UnverifiedManifest flippedManifest = Samples.Parse<UnverifiedManifest>(manifestBytes);
        Assert.Equal(4UL, flippedManifest.Content.Seq);
        Assert.NotEqual(manifest.Hash, flippedManifest.Hash);
        Assert.False(VerifiedManifest.TryVerify(flippedManifest, out _));

        UnverifiedRoster flippedRoster = Samples.Parse<UnverifiedRoster>(rosterBytes);
        Assert.Equal(4UL, flippedRoster.Content.Seq);
        Assert.NotEqual(roster.Hash, flippedRoster.Hash);
        Assert.False(VerifiedRoster.TryVerify(flippedRoster, out _));

        UnverifiedGrant flippedGrant = Samples.Parse<UnverifiedGrant>(grantBytes);
        Assert.Equal(4UL, flippedGrant.Content.Seq);
        Assert.NotEqual(grant.Hash, flippedGrant.Hash);
        Assert.False(VerifiedGrant.TryVerify(flippedGrant, manifest, out _));

        // Unflipped, each verifies again from its bytes alone.
        Assert.True(VerifiedEntry.TryVerify(Samples.Parse<UnverifiedEntry>(entry.Bytes), TestKeys.AliceLaptop.PublicKey, out _));
        Assert.True(VerifiedManifest.TryVerify(Samples.Parse<UnverifiedManifest>(manifest.Bytes), out _));
        Assert.True(VerifiedRoster.TryVerify(Samples.Parse<UnverifiedRoster>(roster.Bytes), out _));
        Assert.True(VerifiedGrant.TryVerify(Samples.Parse<UnverifiedGrant>(grant.Bytes), manifest, out _));
    }

    // §2.2: the signature covers H(domain ‖ body); the domain separates the four structures, so bytes signed as one
    // kind of object never verify as another.
    [Fact]
    public void ASignatureUnderAnotherDomainDoesNotVerify()
    {
        VerifiedEntry entry = Samples.Message(TestKeys.AliceLaptop, Samples.Channel().Content.Package, 0, EntryParents.None);
        byte[] resigned = entry.Bytes.ToArray();
        TestKeys.AliceLaptop.Sign(Digests.Of(SigningDomain.Roster, resigned.AsSpan(0, resigned.Length - 64))).CopyTo(resigned.AsSpan(resigned.Length - 64));

        Assert.False(VerifiedEntry.TryVerify(Samples.Parse<UnverifiedEntry>(resigned), TestKeys.AliceLaptop.PublicKey, out _));
    }

    // §8.9: "A Grant is verified against ownerKey of the current Manifest of its Package"; a Grant Eve signs for
    // Alice's Package is invalid, and a Manifest of another Package cannot be used to check it.
    [Fact]
    public void AGrantVerifiesOnlyAgainstTheOwnerKeyOfItsPackagesManifest()
    {
        VerifiedManifest manifest = Samples.Channel();
        VerifiedGrant genuine = Samples.Grant(manifest, TestKeys.Bob, GrantRights.Write);
        byte[] forged = genuine.Bytes.ToArray();
        TestKeys.Eve.Sign(Digests.Of(SigningDomain.Grant, forged.AsSpan(0, forged.Length - 64))).CopyTo(forged.AsSpan(forged.Length - 64));

        Assert.False(VerifiedGrant.TryVerify(Samples.Parse<UnverifiedGrant>(forged), manifest, out _));
        Assert.Throws<ArgumentException>(() => VerifiedGrant.TryVerify(Samples.Parse<UnverifiedGrant>(genuine.Bytes), Samples.Channel(nonce: 2), out _));
    }

    // §8.6: an Entry verifies against the key of the device it names; checking it against another device's key is the
    // caller's mistake, not the Entry's.
    [Fact]
    public void AnEntryIsCheckedAgainstTheKeyOfTheDeviceItNames()
    {
        VerifiedEntry entry = Samples.Message(TestKeys.AliceLaptop, Samples.Channel().Content.Package, 0, EntryParents.None);
        Assert.Throws<ArgumentException>(() => VerifiedEntry.TryVerify(Samples.Parse<UnverifiedEntry>(entry.Bytes), TestKeys.AlicePhone.PublicKey, out _));
    }

    private static byte[] Flip(ReadOnlyMemory<byte> bytes, int offset)
    {
        byte[] copy = bytes.ToArray();
        copy[offset] ^= 1;
        return copy;
    }
}
