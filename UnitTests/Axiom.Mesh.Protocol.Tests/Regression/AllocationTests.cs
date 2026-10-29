using System;
using System.Buffers;
using Axiom.Mesh.Protocol.Tests.Support;
using Axiom.Mesh.Tests.Shared;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol.Tests.Regression;

/// <summary>The allocation budget of the hot path (plan §3.5): an <c>Object</c> frame is decoded in place.</summary>
public sealed class AllocationTests
{
    // Damage: an Object decoder that copied the payload, hashed through a stream or rented a buffer per frame would
    // allocate a mebibyte per object a node receives; decoding allocates the message and nothing that grows with it.
    [ReleaseFact]
    public void DecodingA1MiBObjectFrameAllocatesOnlyTheMessage()
    {
        var frame = new ReadOnlySequence<byte>(Frames.Encode(MessageSamples.Chunk(Limits.ChunkLength)));
        using var runner = new CodecRunner<MeshMessage, MeshMessage>(Amp.Definition);
        runner.Decode(frame, isCompleted: true);

        long before = GC.GetAllocatedBytesForCurrentThread();
        DecodeOutcome<MeshMessage> outcome = runner.Decode(frame, isCompleted: true);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsType<ObjectMessage>(outcome.Message);
        Assert.InRange(allocated, 1, 128);
    }
}
