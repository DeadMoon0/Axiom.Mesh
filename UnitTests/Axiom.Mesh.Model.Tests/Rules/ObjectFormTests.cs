using System;
using System.Buffers;
using System.Text;
using Axiom.Mesh.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §1.4, §2.3, §5.2, §7.6 and §8: the common form of objects and the fields every object shares.</summary>
public sealed class ObjectFormTests
{
    // §7.6: "hash MUST equal H(object); a mismatch is a violation", and nothing parses without the hash checked first.
    [Fact]
    public void BytesAreNamedByTheirSha256AndAClaimedHashThatDiffersIsRefused()
    {
        byte[] bytes = ObjectBuilders.Chunk("x"u8).Bytes.ToArray();
        ObjectHash name = new(new Hash32(System.Security.Cryptography.SHA256.HashData(bytes)));

        Assert.True(HashedObject.TryVerify(bytes, name, out HashedObject hashed));
        Assert.Equal(name, hashed.Hash);
        Assert.False(HashedObject.TryVerify(bytes, RawObjects.Hash(0), out _));
    }

    // §8.1: "An object whose version is not 1, or whose body does not parse exactly to its end (no trailing bytes) … is
    // malformed." An object too short for its tag and version is too.
    [Fact]
    public void AnObjectWithAnotherVersionTrailingBytesOrNoHeaderIsMalformed()
    {
        Assert.Contains("version", Samples.Fault(RawObjects.Unsigned((byte)ObjectTag.Chunk, o => o.Write("x"u8), version: 2)).Detail, StringComparison.Ordinal);
        Assert.Contains("version", Samples.Fault(RawObjects.Unsigned(99, o => o.Write("x"u8), version: 0)).Detail, StringComparison.Ordinal);

        byte[] grant = Samples.Grant(Samples.Channel(), TestKeys.Bob, GrantRights.Read).Bytes.ToArray();
        byte[] trailing = [.. grant, 0];
        Assert.Contains("follow", Samples.Fault(trailing).Detail, StringComparison.Ordinal);

        Samples.Fault(Array.Empty<byte>());
        Samples.Fault(new byte[] { (byte)ObjectTag.Chunk });
    }

    // §1.4: "an object whose tag is unknown is opaque: it is verified by its hash only, stored, replicated … and never
    // interpreted"; tag 8 is reserved, 9 to 255 for extensions, and 0 is assigned to nothing.
    [Theory]
    // Tag 0, assigned to nothing.
    [InlineData((byte)0)]
    // Tag 8, reserved for a sealed object.
    [InlineData((byte)8)]
    // The largest extension tag.
    [InlineData((byte)255)]
    public void AnObjectWithAnUnknownTagIsOpaqueAndStorable(byte tag)
    {
        OpaqueObject opaque = Samples.Parse<OpaqueObject>(RawObjects.Unsigned(tag, o => o.Write("anything at all"u8)));
        Assert.Equal(tag, opaque.RawTag);
        Assert.IsAssignableFrom<StorableObject>(opaque);
    }

    // §2.3: "a pubkey whose first byte is not 0x04 or whose point is not on the curve is malformed"; §8.9: "granteeId
    // MUST equal H(granteeKey)"; "rights … other values: malformed".
    [Fact]
    public void AGrantWithAKeyOffTheCurveAMismatchedIdOrUndefinedRightsIsMalformed()
    {
        byte[] Grant(ReadOnlySpan<byte> key, UserId grantee, byte rights)
        {
            byte[] keyCopy = key.ToArray();
            return RawObjects.FakeSigned(ObjectTag.Grant, output =>
            {
                var writer = new WireWriter(output);
                Samples.Channel().Content.Package.Write(ref writer);
                grantee.Write(ref writer);
                writer.Bytes(keyCopy);
                writer.UInt8(rights);
                writer.UInt64BigEndian(1);
            });
        }

        PublicKey bob = TestKeys.Bob.PublicKey;
        Samples.Parse<UnverifiedGrant>(Grant(bob.Encoded, bob.AsUserId(), 2));
        Assert.Contains("rights", Samples.Fault(Grant(bob.Encoded, bob.AsUserId(), 3)).Detail, StringComparison.Ordinal);
        Assert.Contains("granteeId", Samples.Fault(Grant(bob.Encoded, TestKeys.Eve.PublicKey.AsUserId(), 1)).Detail, StringComparison.Ordinal);

        byte[] compressedPrefix = bob.Encoded.ToArray();
        compressedPrefix[0] = 0x02;
        Samples.Fault(Grant(compressedPrefix, bob.AsUserId(), 1));

        byte[] offCurve = bob.Encoded.ToArray();
        offCurve[64] ^= 1;
        Samples.Fault(Grant(offCurve, bob.AsUserId(), 1));
        Assert.False(PublicKey.TryParse(offCurve, out _));
    }

    // §5.2: "bool: u8, 0 or 1; any other value is malformed"; "string(max): … no NUL, valid UTF-8"; "In an object, a value
    // outside its bounds … makes the object malformed".
    [Fact]
    public void ABoolOtherThan0Or1AndAStringWithNulOrInvalidUtf8AreMalformed()
    {
        byte[] Roster(byte removed, byte[] name) => RawObjects.FakeSigned(ObjectTag.Roster, output =>
        {
            var writer = new WireWriter(output);
            TestKeys.Alice.PublicKey.AsUserId().Write(ref writer);
            TestKeys.Alice.PublicKey.Write(ref writer);
            writer.UInt64BigEndian(1);
            writer.UInt16BigEndian(1);
            TestKeys.AliceLaptop.PublicKey.AsDeviceId().Write(ref writer);
            TestKeys.AliceLaptop.PublicKey.Write(ref writer);
            RawObjects.String(ref writer, name);
            writer.UInt64BigEndian(0);
            writer.UInt8(removed);
        });

        Samples.Parse<UnverifiedRoster>(Roster(1, Encoding.UTF8.GetBytes("laptop")));
        Samples.Fault(Roster(2, Encoding.UTF8.GetBytes("laptop")));
        Samples.Fault(Roster(0, [(byte)'a', 0, (byte)'b']));
        Samples.Fault(Roster(0, [0xC3, 0x28]));
    }

    // §8.7: "retention: … other values are read as 0"; "kind … the Package kind": an unknown kind is carried (§9.2).
    [Fact]
    public void AManifestsUnknownRetentionReadsAsDefault_AndAnUnknownKindIsCarried()
    {
        VerifiedManifest manifest = new ManifestDraft { Nonce = Samples.Nonce(4), Kind = (PackageKind)77, Seq = 1, Name = "x", Created = 0, Retention = Retention.LatestOnly }.Sign(TestKeys.Alice);
        byte[] bytes = manifest.Bytes.ToArray();
        int retentionOffset = 2 + 32 + 32 + 65 + 32 + 2 + 8 + 2 + 1 + 8;
        Assert.Equal((byte)Retention.LatestOnly, bytes[retentionOffset]);
        bytes[retentionOffset] = 9;

        ManifestContent content = Samples.Parse<UnverifiedManifest>(bytes).Content;
        Assert.Equal(Retention.Default, content.Retention);
        Assert.Equal((PackageKind)77, content.Kind);
    }

    // §8.7: "branches … ascending by name; names unique".
    [Fact]
    public void AManifestsBranchesMustAscendByNameWithoutRepeats()
    {
        byte[] Manifest(params string[] branches) => RawObjects.FakeSigned(ObjectTag.Manifest, output =>
        {
            var writer = new WireWriter(output);
            PublicKey key = TestKeys.Alice.PublicKey;
            PackageId.Derive(key.AsUserId(), Samples.Nonce(1)).Write(ref writer);
            key.AsUserId().Write(ref writer);
            key.Write(ref writer);
            Samples.Nonce(1).Write(ref writer);
            writer.UInt16BigEndian(1);
            writer.UInt64BigEndian(1);
            RawObjects.String(ref writer, "n"u8);
            writer.UInt64BigEndian(0);
            writer.UInt8(0);
            writer.UInt16BigEndian((ushort)branches.Length);
            foreach (string branch in branches)
            {
                RawObjects.String(ref writer, Encoding.UTF8.GetBytes(branch));
                RawObjects.Hash(0).Write(ref writer);
            }
        });

        Assert.Equal(2, Samples.Parse<UnverifiedManifest>(Manifest("beta", "main")).Content.Branches.Count);
        Samples.Fault(Manifest("main", "beta"));
        Samples.Fault(Manifest("main", "main"));
        Assert.Throws<ArgumentException>(() => new ManifestDraft { Nonce = Samples.Nonce(1), Kind = PackageKind.Archive, Seq = 1, Name = "n", Created = 0, Branches = [new("main", default), new("main", default)] }.Sign(TestKeys.Alice));
    }

    // §8.8: "devices: … ascending by deviceId; deviceIds unique"; "deviceId: MUST equal H(key)".
    [Fact]
    public void ARostersDevicesMustAscendByIdWithIdsDerivedFromTheirKeys()
    {
        PublicKey[] keys = [TestKeys.AliceLaptop.PublicKey, TestKeys.AlicePhone.PublicKey];
        Array.Sort(keys, (a, b) => a.AsDeviceId().CompareTo(b.AsDeviceId()));

        byte[] Roster(PublicKey first, PublicKey second, DeviceId? firstId = null) => RawObjects.FakeSigned(ObjectTag.Roster, output =>
        {
            var writer = new WireWriter(output);
            TestKeys.Alice.PublicKey.AsUserId().Write(ref writer);
            TestKeys.Alice.PublicKey.Write(ref writer);
            writer.UInt64BigEndian(1);
            writer.UInt16BigEndian(2);
            foreach ((PublicKey key, DeviceId id) in new[] { (first, firstId ?? first.AsDeviceId()), (second, second.AsDeviceId()) })
            {
                id.Write(ref writer);
                key.Write(ref writer);
                RawObjects.String(ref writer, "d"u8);
                writer.UInt64BigEndian(0);
                writer.UInt8(0);
            }
        });

        Samples.Parse<UnverifiedRoster>(Roster(keys[0], keys[1]));
        Samples.Fault(Roster(keys[1], keys[0]));
        Samples.Fault(Roster(keys[0], keys[0]));
        Samples.Fault(Roster(keys[0], keys[1], firstId: new DeviceId(RawObjects.Hash(0).Value)));
    }
}
