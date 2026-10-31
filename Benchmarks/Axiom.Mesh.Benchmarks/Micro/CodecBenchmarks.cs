using System;
using System.Buffers;
using System.Linq;
using Axiom.Mesh.Protocol;
using Axiom.Mesh.Testing;
using Axiom.ProtoStream.Codecs;
using BenchmarkDotNet.Attributes;
using WireState = Axiom.Mesh.Protocol.PackageState;

namespace Axiom.Mesh.Benchmarks.Micro;

/// <summary>
/// The AMP/1 frame codec (plan §5: a small message under 1 µs with only the message allocated; an Object at 2 GiB/s or
/// more with one copy per direction): encoding into a reused buffer and decoding one frame, for <c>Hello</c>, a
/// <c>PackageChanged</c> with 256 Heads, an Entry as an <c>Object</c>, and a 1 MiB Chunk as an <c>Object</c>.
/// </summary>
[MemoryDiagnoser]
public class CodecBenchmarks
{
    private readonly ArrayBufferWriter<byte> output = new(Limits.MaxObjectLength + 64);
    private CodecRunner<MeshMessage, MeshMessage> runner = null!;
    private MeshMessage message = null!;
    private ReadOnlySequence<byte> frame;

    /// <summary>The message under test.</summary>
    [Params("Hello", "PackageState256Heads", "EntryObject", "ChunkObject1MiB")]
    public string Message { get; set; } = "Hello";

    /// <summary>Builds the message and its frame.</summary>
    [GlobalSetup]
    public void Setup()
    {
        runner = new CodecRunner<MeshMessage, MeshMessage>(Amp.Definition);
        message = Message switch
        {
            "Hello" => new Hello(TestKeys.AliceLaptop.PublicKey.AsDeviceId(), TestKeys.Alice.PublicKey.AsUserId(), 3, Capabilities.FollowsNewGrants, ["axiom-mesh-sync/1"]),
            "PackageState256Heads" => new PackageChanged(State(Limits.MaxPackageHeads)),
            "EntryObject" => EntryMessage(),
            "ChunkObject1MiB" => ObjectMessage.From(ObjectBuilders.Chunk(Samples.Bytes(Limits.ChunkLength, 3))),
            _ => throw new InvalidOperationException(Message),
        };
        output.ResetWrittenCount();
        runner.Encode(message, output);
        frame = new ReadOnlySequence<byte>(output.WrittenSpan.ToArray());
    }

    /// <summary>Cleans up the runner.</summary>
    [GlobalCleanup]
    public void Cleanup() => runner.Dispose();

    /// <summary>Encodes the message into a reused buffer.</summary>
    /// <returns>The frame's length.</returns>
    [Benchmark]
    public int Encode()
    {
        output.ResetWrittenCount();
        runner.Encode(message, output);
        return output.WrittenCount;
    }

    /// <summary>Decodes one frame.</summary>
    /// <returns>The message.</returns>
    [Benchmark]
    public MeshMessage? Decode() => runner.Decode(frame, isCompleted: true).Message;

    // An Entry as the store sends it: the send path takes stored bytes and their hash.
    private static ObjectMessage EntryMessage()
    {
        VerifiedEntry entry = new EntryDraft
        {
            Package = new PackageId(Hash32.Of("benchmark package"u8)),
            Clock = 41,
            Parents = EntryParents.Of(ObjectHash.Of("parent"u8)),
            Body = new EntryBody(EntryKind.Message, new byte[48], EntryReferences.None, default),
        }.Sign(TestKeys.AliceLaptop);
        return ObjectMessage.FromStored(entry.Hash, entry.Bytes);
    }

    // A Package with the most Heads a state carries, ascending by device id.
    private static WireState State(int heads)
    {
        Head[] all = [.. Enumerable.Range(0, heads).Select(i => new Head(new DeviceId(Hash32.Of(BitConverter.GetBytes(i))), ObjectHash.Of(BitConverter.GetBytes(~i)), (ulong)i)).OrderBy(h => h.Device)];
        return new WireState(new PackageId(Hash32.Of("benchmark package"u8)), ObjectHash.Of("manifest"u8), 7, [], all);
    }
}
