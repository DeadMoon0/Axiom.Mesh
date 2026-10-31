using System;
using System.Buffers;
using System.Linq;
using System.Security.Cryptography;
using Axiom.Mesh.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>
/// SPEC.md §5.2, §8 and §15: every bound of an object is accepted at the limit and makes the object malformed one over.
/// The message half of the same rule (a violation) is proven by the protocol's limit tests.
/// </summary>
public sealed class ObjectBoundsTests
{
    private static readonly Lazy<PublicKey[]> ManyDeviceKeys = new(() =>
        [.. Enumerable.Range(0, Limits.MaxRosterDevices + 1).Select(_ => NewKey()).OrderBy(k => k.AsDeviceId())]);

    // §15: "every limit refused one over and accepted at the limit, with the message-versus-object distinction of §5.2";
    // §5.2: "In an object, [a value outside its bounds] makes the object malformed." Each row names a bound and the value
    // SPEC.md gives it.
    [Theory]
    // §8.1: an object is at most 4 MiB.
    [InlineData("object", 4_194_304)]
    // §8.2: a Chunk holds at most 1 MiB.
    [InlineData("chunk data", 1_048_576)]
    // §8.3: a ChunkList names at most 32 768 Chunks.
    [InlineData("chunklist chunks", 32_768)]
    // §8.4: a Tree holds at most 8 192 entries.
    [InlineData("tree entries", 8_192)]
    // §8.4: a Tree entry's name is at most 255 bytes.
    [InlineData("tree name", 255)]
    // §8.6: an Entry has at most 2 parents.
    [InlineData("entry parents", 2)]
    // §8.6: an Entry's payload is at most 1 MiB.
    [InlineData("entry payload", 1_048_576)]
    // §8.6: an Entry references at most 64 objects.
    [InlineData("entry references", 64)]
    // §8.7: a Manifest's name is at most 255 bytes.
    [InlineData("manifest name", 255)]
    // §8.7: a Manifest has at most 64 branches.
    [InlineData("manifest branches", 64)]
    // §8.7: a branch name is at most 64 bytes.
    [InlineData("branch name", 64)]
    // §8.8: a Roster lists at most 256 devices.
    [InlineData("roster devices", 256)]
    // §8.8: a device's name is at most 64 bytes.
    [InlineData("device name", 64)]
    public void EveryObjectBoundIsAcceptedAtTheLimitAndMalformedOneOver(string bound, int limit)
    {
        Samples.Parse<ParsedObject>(Build(bound, limit));
        Assert.Equal(ObjectFaultKind.Malformed, Samples.Fault(Build(bound, limit + 1)).Kind);
    }

    // §8.2: a Chunk holds "1 to 1 048 576 bytes": an empty one is malformed too.
    [Fact]
    public void AnEmptyChunkIsMalformed() => Samples.Fault(RawObjects.Unsigned((byte)ObjectTag.Chunk, _ => { }));

    private static byte[] Build(string bound, int n) => bound switch
    {
        "object" => RawObjects.Unsigned(200, o => o.Write(new byte[n - 2])),
        "chunk data" => RawObjects.Unsigned((byte)ObjectTag.Chunk, o => o.Write(new byte[n])),
        "chunklist chunks" => RawObjects.Unsigned((byte)ObjectTag.ChunkList, o => Write(o, (ref WireWriter w) =>
        {
            w.UInt64BigEndian(0);
            w.UInt32BigEndian((uint)n);
            w.Bytes(new byte[n * Hash32.Length]);
        })),
        "tree entries" => Tree(n, 1),
        "tree name" => Tree(1, n),
        "entry parents" => Entry(n, 9, []),
        "entry payload" => Entry(1, 9, new byte[n]),
        "entry references" => RawObjects.FakeSigned(ObjectTag.Entry, RawObjects.EntryBody(
            Samples.Channel().Content.Package,
            TestKeys.AliceLaptop.PublicKey.AsDeviceId(),
            9,
            [RawObjects.Hash(1)],
            9,
            [.. Enumerable.Range(0, n).Select(Ascending)],
            default,
            [])),
        "manifest name" => Manifest(n, 0, 1),
        "manifest branches" => Manifest(1, n, 1),
        "branch name" => Manifest(1, 1, n),
        "roster devices" => Roster(n, 1),
        "device name" => Roster(1, n),
        _ => throw new ArgumentException("Unknown bound " + bound, nameof(bound)),
    };

    private delegate void Fields(ref WireWriter writer);

    private static void Write(IBufferWriter<byte> output, Fields fields)
    {
        var writer = new WireWriter(output);
        fields(ref writer);
    }

    // A string of n bytes of 'a' (ASCII is one byte per character).
    private static void Text(ref WireWriter writer, int n) => RawObjects.String(ref writer, Enumerable.Repeat((byte)'a', n).ToArray());

    // The i-th of a list of distinct hashes in ascending order: i as a big-endian number in the last two bytes.
    private static ObjectHash Ascending(int i)
    {
        byte[] bytes = new byte[Hash32.Length];
        bytes[^2] = (byte)(i >> 8);
        bytes[^1] = (byte)i;
        return new ObjectHash(new Hash32(bytes));
    }

    private static byte[] Entry(int parents, ushort kind, byte[] payload) =>
        RawObjects.FakeSigned(ObjectTag.Entry, RawObjects.EntryBody(
            Samples.Channel().Content.Package,
            TestKeys.AliceLaptop.PublicKey.AsDeviceId(),
            9,
            [.. Enumerable.Range(1, parents).Select(i => RawObjects.Hash((byte)i))],
            kind,
            payload));

    private static byte[] Tree(int entries, int nameLength) => RawObjects.Unsigned((byte)ObjectTag.Tree, o => Write(o, (ref WireWriter w) =>
    {
        w.UInt16BigEndian((ushort)entries);
        for (int i = 0; i < entries; i++)
        {
            string name = i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
            RawObjects.String(ref w, System.Text.Encoding.ASCII.GetBytes(name.PadRight(nameLength, 'a')[..Math.Max(nameLength, 5)]));
            w.UInt8(1);
            RawObjects.Hash(1).Write(ref w);
            w.UInt64BigEndian(0);
        }
    }));

    private static byte[] Manifest(int nameLength, int branches, int branchNameLength) => RawObjects.FakeSigned(ObjectTag.Manifest, o => Write(o, (ref WireWriter w) =>
    {
        PublicKey key = TestKeys.Alice.PublicKey;
        PackageId.Derive(key.AsUserId(), Samples.Nonce(1)).Write(ref w);
        key.AsUserId().Write(ref w);
        key.Write(ref w);
        Samples.Nonce(1).Write(ref w);
        w.UInt16BigEndian(1);
        w.UInt64BigEndian(1);
        Text(ref w, nameLength);
        w.UInt64BigEndian(0);
        w.UInt8(0);
        w.UInt16BigEndian((ushort)branches);
        for (int i = 0; i < branches; i++)
        {
            string name = i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            RawObjects.String(ref w, System.Text.Encoding.ASCII.GetBytes(name.PadRight(branchNameLength, 'a')));
            RawObjects.Hash(0).Write(ref w);
        }
    }));

    private static byte[] Roster(int devices, int nameLength) => RawObjects.FakeSigned(ObjectTag.Roster, o => Write(o, (ref WireWriter w) =>
    {
        TestKeys.Alice.PublicKey.AsUserId().Write(ref w);
        TestKeys.Alice.PublicKey.Write(ref w);
        w.UInt64BigEndian(1);
        w.UInt16BigEndian((ushort)devices);
        foreach (PublicKey key in ManyDeviceKeys.Value.Take(devices))
        {
            key.AsDeviceId().Write(ref w);
            key.Write(ref w);
            Text(ref w, nameLength);
            w.UInt64BigEndian(0);
            w.UInt8(0);
        }
    }));

    private static PublicKey NewKey()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        Assert.True(PublicKey.TryParse([0x04, .. parameters.Q.X!, .. parameters.Q.Y!], out PublicKey? publicKey));
        return publicKey;
    }
}
