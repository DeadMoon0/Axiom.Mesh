using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Axiom.Mesh.Protocol.Tests.Support;
using Axiom.ProtoStream.Codecs;
using Axiom.ProtoStream.Errors;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>The <c>Object</c> message (SPEC.md §7.6): only storable objects are sent, and a received one is checked and not copied.</summary>
public sealed class ObjectMessageTests
{
    // §7.6: "hash MUST equal H(object); a mismatch is a violation."
    [Fact]
    public void AnObjectWhoseHashIsNotItsSha256IsAViolation()
    {
        byte[] frame = Frames.Encode(MessageSamples.Chunk(100));
        frame[6] ^= 0x01;

        DecodeOutcome<MeshMessage> outcome = Frames.Decode(frame);

        Assert.Equal(DecodeStatus.Invalid, outcome.Status);
    }

    // §7.6: "A receiver MUST verify the hash ... before storing, holding or forwarding it"; plan §3.5: the object is
    // hashed on the frame view, never copied by the codec. The view is valid until the next read, and says so.
    [Fact]
    public void AReceivedObjectIsAViewOfItsFrame_ValidUntilTheNextRead()
    {
        byte[] first = Frames.Encode(MessageSamples.Chunk(Limits.ChunkLength));
        byte[] second = Frames.Encode(new Ping(1));
        using var runner = new CodecRunner<MeshMessage, MeshMessage>(Amp.Definition);

        ObjectMessage received = Assert.IsType<ObjectMessage>(runner.Decode(new ReadOnlySequence<byte>(first), isCompleted: true).Message);
        Assert.True(received.IsCurrent);
        Assert.True(MemoryMarshal.TryGetArray(received.Bytes, out ArraySegment<byte> view));
        Assert.Same(first, view.Array);
        Assert.Equal(6 + 32 + 4, view.Offset);
        Assert.True(Objects.TryParse(received.Object, out ParsedObject? parsed, out _));
        Assert.IsType<Chunk>(parsed);

        runner.Decode(new ReadOnlySequence<byte>(second), isCompleted: true);

        Assert.False(received.IsCurrent);
        Assert.Throws<StaleMessageException>(() => received.Hash);
        Assert.Throws<StaleMessageException>(() => received.Bytes);
        Assert.Throws<StaleMessageException>(() => received.Object);
    }

    // §7.6, §10.6: nothing unverified is forwarded; an Object to send is made from a storable object only, without a copy.
    [Fact]
    public void AnObjectIsSentOnlyFromAStorableObjectAndItsBytesAreNotCopied()
    {
        Chunk chunk = ObjectBuilders.Chunk([1, 2, 3]);

        ObjectMessage message = ObjectMessage.From(chunk);

        Assert.Equal(chunk.Hash, message.Hash);
        Assert.True(message.Bytes.Equals(chunk.Bytes));
        Assert.True(message.IsCurrent);
        Assert.Equal(chunk.Hash, message.Object.Hash);
        Assert.Null(typeof(ObjectMessage).GetConstructor(Type.EmptyTypes));
        Assert.Empty(typeof(ObjectMessage).GetConstructors());
    }
}
