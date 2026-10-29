using System;
using System.Linq;
using Axiom.Mesh.Protocol.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>
/// Every bound of an AMP/1 message, at the bound and one over (SPEC.md §5.2, §7, §12.3, §15): one over is a violation in
/// a message, malformed in an object (which is not a violation of the message carrying it), and a close for <c>Bye</c>
/// (§6.1). The bodies are written by hand, as a peer that breaks the rules would.
/// </summary>
public sealed class LimitTests
{
    /// <summary>What one over a bound does.</summary>
    public enum OverLimit
    {
        /// <summary>The frame is a violation.</summary>
        Violation,

        /// <summary>The frame reads as a <c>Bye</c> without reason: a malformed <c>Bye</c> is a close (§6.1).</summary>
        Close,

        /// <summary>The message is well-formed; the object it carries is malformed (§5.2, §8.1).</summary>
        MalformedObject,
    }

    // §5.2: "A receiver MUST detect a length or count over its maximum from the length field before reading the content.
    // In a message, a value outside its bounds is a violation. In an object, it makes the object malformed."
    [Theory]
    // §5.1 frame length, 4 198 400 bytes
    [InlineData(nameof(Limits.MaxFrameLength), OverLimit.Violation)]
    // §7.1 Hello.services, 64 names
    [InlineData(nameof(Limits.MaxHelloServices), OverLimit.Violation)]
    // §7.1, §7.10 service names, 64 bytes
    [InlineData(nameof(Limits.MaxServiceNameLength), OverLimit.Violation)]
    // §7.2 PackageStates per page, 64
    [InlineData(nameof(Limits.MaxPackagesPerPage), OverLimit.Violation)]
    // §7.2 PackageState.grants, 256
    [InlineData(nameof(Limits.MaxPackageGrants), OverLimit.Violation)]
    // §7.2 PackageState.heads, 256
    [InlineData(nameof(Limits.MaxPackageHeads), OverLimit.Violation)]
    // §7.4 Have, 1 024 hashes
    [InlineData(nameof(Limits.MaxHaveHashes), OverLimit.Violation)]
    // §7.5 Want, 64 hashes
    [InlineData(nameof(Limits.MaxWantHashes), OverLimit.Violation)]
    // §7.8 Cancel, 1 024 hashes
    [InlineData(nameof(Limits.MaxCancelHashes), OverLimit.Violation)]
    // §7.6 Object.object, blob(4194304)
    [InlineData(nameof(Limits.MaxObjectLength), OverLimit.Violation)]
    // §7.13 Bye.detail, 256 bytes; a malformed Bye is a close (§6.1)
    [InlineData(nameof(Limits.MaxByeDetailLength), OverLimit.Close)]
    // §8.2 a Chunk's data, 1 MiB: malformed in the object, not a violation of the Object message
    [InlineData(nameof(Limits.ChunkLength), OverLimit.MalformedObject)]
    public void EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit_AViolationInAMessageMalformedInAnObject(string limit, OverLimit over)
    {
        (byte[] atLimit, byte[] oneOver) = FramesFor(limit);

        DecodeOutcome<MeshMessage> accepted = Frames.Decode(atLimit);
        Assert.Equal(DecodeStatus.Message, accepted.Status);
        Assert.False(accepted.Message is Bye { Reason: ByeReason.Unspecified, Detail: "" }, "The frame at the limit read as a close.");
        if (accepted.Message is ObjectMessage atLimitObject)
            Assert.True(Objects.TryParse(atLimitObject.Object, out _, out _), "The object at the limit is malformed.");

        DecodeOutcome<MeshMessage> refused = Frames.Decode(oneOver);
        switch (over)
        {
            case OverLimit.Violation:
                Assert.Equal(DecodeStatus.Invalid, refused.Status);
                break;
            case OverLimit.Close:
                Bye bye = Assert.IsType<Bye>(refused.Message);
                Assert.Equal(ByeReason.Unspecified, bye.Reason);
                Assert.Equal(string.Empty, bye.Detail);
                break;
            default:
                ObjectMessage carried = Assert.IsType<ObjectMessage>(refused.Message);
                Assert.False(Objects.TryParse(carried.Object, out _, out ObjectFault fault));
                Assert.Equal(ObjectFaultKind.Malformed, fault.Kind);
                break;
        }
    }

    private static (byte[] AtLimit, byte[] OneOver) FramesFor(string limit) => limit switch
    {
        nameof(Limits.MaxFrameLength) => (
            Frames.Raw(MessageIds.FirstExtension, new byte[Limits.MaxFrameLength - 2]),
            Frames.WithLength(Limits.MaxFrameLength + 1, [0, (byte)MessageIds.FirstExtension])),
        nameof(Limits.MaxHelloServices) => Pair(MessageIds.Hello, Limits.MaxHelloServices, n => Hello(n)),
        nameof(Limits.MaxServiceNameLength) => Pair(MessageIds.ServiceOpen, Limits.MaxServiceNameLength, n => new Bytes().Text(new string('s', n)).ToArray()),
        nameof(Limits.MaxPackagesPerPage) => Pair(MessageIds.Packages, Limits.MaxPackagesPerPage, n => Enumerable.Range(0, n).Aggregate(new Bytes().U16(0).U8(1).U16(n), (b, _) => State(b, 0, 0)).ToArray()),
        nameof(Limits.MaxPackageGrants) => Pair(MessageIds.PackageChanged, Limits.MaxPackageGrants, n => State(new Bytes(), n, 0).ToArray()),
        nameof(Limits.MaxPackageHeads) => Pair(MessageIds.PackageChanged, Limits.MaxPackageHeads, n => State(new Bytes(), 0, n).ToArray()),
        nameof(Limits.MaxHaveHashes) => Pair(MessageIds.Have, Limits.MaxHaveHashes, n => new Bytes().HashList(n).ToArray()),
        nameof(Limits.MaxWantHashes) => Pair(MessageIds.Want, Limits.MaxWantHashes, n => new Bytes().HashList(n).ToArray()),
        nameof(Limits.MaxCancelHashes) => Pair(MessageIds.Cancel, Limits.MaxCancelHashes, n => new Bytes().HashList(n).ToArray()),
        nameof(Limits.MaxObjectLength) => Pair(MessageIds.Object, Limits.MaxObjectLength, n => ObjectBody([OpaqueTag, global::Axiom.Mesh.Objects.Version, .. new byte[n - 2]])),
        nameof(Limits.MaxByeDetailLength) => Pair(MessageIds.Bye, Limits.MaxByeDetailLength, n => new Bytes().U8((byte)ByeReason.Violation).Text(new string('d', n)).ToArray()),
        nameof(Limits.ChunkLength) => Pair(MessageIds.Object, Limits.ChunkLength, n => ObjectBody([(byte)ObjectTag.Chunk, global::Axiom.Mesh.Objects.Version, .. new byte[n]])),
        _ => throw new ArgumentException($"No frames for {limit}.", nameof(limit)),
    };

    // An extension tag (SPEC.md §14.6): the object is opaque, well-formed at any length up to the object bound.
    private const byte OpaqueTag = 9;

    private static (byte[], byte[]) Pair(ushort id, int bound, Func<int, byte[]> body) =>
        (Frames.Raw(id, body(bound)), Frames.Raw(id, body(bound + 1)));

    private static byte[] Hello(int services)
    {
        var body = new Bytes().U16(1).Fill(32, 1).Fill(32, 2).U64(1).U32(0).U16(services);
        for (int i = 0; i < services; i++)
            body.Text("s");
        return body.ToArray();
    }

    private static Bytes State(Bytes body, int grants, int heads)
    {
        body.Fill(32, 3).Fill(32, 4).U64(1).HashList(grants).U16(heads);
        foreach (ObjectHash device in MessageSamples.Hashes(heads))
            body.Raw(Raw(device)).Fill(32, 5).U64(9);
        return body;
    }

    private static byte[] ObjectBody(byte[] objectBytes)
    {
        var body = new Bytes().Raw(Raw(ObjectHash.Of(objectBytes))).U32((uint)objectBytes.Length).Raw(objectBytes);
        return body.ToArray();
    }

    private static byte[] Raw(ObjectHash hash)
    {
        byte[] raw = new byte[Hash32.Length];
        hash.Value.CopyTo(raw);
        return raw;
    }
}
