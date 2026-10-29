using System;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Protocol.Tests.Support;

/// <summary>Messages of every id of SPEC.md §14.1, with their lists empty and at their bounds, for round trips and fuzzing.</summary>
internal static class MessageSamples
{
    /// <summary>A hash of 32 equal bytes.</summary>
    public static ObjectHash Hash(byte fill) => new(new Hash32(Enumerable.Repeat(fill, Hash32.Length).ToArray()));

    /// <summary><paramref name="count"/> distinct hashes, strictly ascending.</summary>
    public static ObjectHash[] Hashes(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new ObjectHash(new Hash32([.. new byte[28], (byte)(i >> 24), (byte)(i >> 16), (byte)(i >> 8), (byte)i])))];

    /// <summary><paramref name="count"/> heads, strictly ascending by device id.</summary>
    public static Head[] Heads(int count) =>
        [.. Hashes(count).Select((h, i) => new Head(new DeviceId(h.Value), Hash((byte)i), (ulong)i))];

    /// <summary>A Package state with <paramref name="grants"/> grants and <paramref name="heads"/> heads.</summary>
    public static PackageState State(int grants, int heads) =>
        new(new PackageId(Hash(0xA1).Value), Hash(0xB2), 7, Hashes(grants), Heads(heads));

    /// <summary>Alice's laptop greeting with the given services.</summary>
    public static Hello Hello(params string[] services) =>
        new(TestKeys.AliceLaptop.PublicKey.AsDeviceId(), TestKeys.Alice.PublicKey.AsUserId(), 3, Capabilities.FollowsNewGrants | Capabilities.FullMirror, services);

    /// <summary>A Chunk of <paramref name="length"/> bytes as an <c>Object</c>.</summary>
    public static ObjectMessage Chunk(int length) =>
        ObjectMessage.From(ObjectBuilders.Chunk(Enumerable.Range(0, length).Select(i => (byte)(i * 31)).ToArray()));

    /// <summary>Every message of §14.1 at least once, a 1 MiB <c>Object</c>, and the two ends of the extension range.</summary>
    public static IReadOnlyList<MeshMessage> All()
    {
        string longest = new('s', Limits.MaxServiceNameLength);
        return
        [
            Hello(),
            Hello([.. Enumerable.Range(0, Limits.MaxHelloServices).Select(i => $"axiom-mesh-svc{i}/1")]),
            Hello(longest, "axiom-mesh-chat/1", "ü-ß-é"),
            new Packages(0, last: true, []),
            new Packages(3, last: false, [State(0, 0), State(Limits.MaxPackageGrants, 2), State(1, Limits.MaxPackageHeads)]),
            new PackageChanged(State(2, 3)),
            new Have([]),
            new Have(Hashes(Limits.MaxHaveHashes)),
            new Want(Hashes(Limits.MaxWantHashes)),
            Chunk(1),
            Chunk(Limits.ChunkLength),
            new NotFound(Hash(7)),
            new Cancel(Hashes(Limits.MaxCancelHashes)),
            new RosterQuery(TestKeys.Bob.PublicKey.AsUserId()),
            new ServiceOpen("axiom-mesh-sync/1"),
            new ServiceAccepted(longest),
            new ServiceRefused("axiom-mesh-chat/1", ServiceRefusal.NotAllowed),
            new Ping(0),
            new Pong(ulong.MaxValue),
            new Bye(ByeReason.Idle, string.Empty),
            new Bye(ByeReason.Violation, new string('d', Limits.MaxByeDetailLength)),
            new RosterNotFound(TestKeys.Eve.PublicKey.AsUserId()),
            new Extension(MessageIds.FirstExtension),
            new Extension(MessageIds.LastExtension),
        ];
    }

    /// <summary>A message as text: its type and its frame in hex. A received <c>Object</c> must still be current.</summary>
    public static string Describe(MeshMessage message) => $"{message.GetType().Name} {Convert.ToHexString(Frames.Encode(message))}";

    /// <summary>Whether two messages say the same: their frames are equal (an <c>Object</c> by hash and bytes, without re-encoding a mebibyte).</summary>
    public static bool Same(MeshMessage expected, MeshMessage actual)
    {
        if (expected is ObjectMessage sent && actual is ObjectMessage received)
            return sent.Hash == received.Hash && sent.Bytes.Span.SequenceEqual(received.Bytes.Span);
        return expected.GetType() == actual.GetType() && Frames.Encode(expected).AsSpan().SequenceEqual(Frames.Encode(actual));
    }
}
