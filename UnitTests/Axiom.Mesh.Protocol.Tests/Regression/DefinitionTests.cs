using System;
using Axiom.Mesh.Protocol.Tests.Support;
using Axiom.ProtoStream.Testing;

namespace Axiom.Mesh.Protocol.Tests.Regression;

/// <summary>The two definitions as ProtoStream checks them, and the bytes a frame starts with.</summary>
public sealed class DefinitionTests
{
    // Damage: an opted-out limit (no idle timeout, no first-message timeout) or a state that accepts nothing would leave a
    // session open to a silent peer for ever; the linter names both.
    [Fact]
    public void BothDefinitionsHaveNoWarnings()
    {
        DefinitionAssert.NoWarnings(Amp.Definition);
        DefinitionAssert.NoWarnings(PairProtocol.Definition);
        Assert.Equal([Amp.Connected, Amp.Established, Amp.Closed], Amp.Definition.States);
        Assert.Equal(TimeSpan.FromSeconds(Limits.FirstHelloSeconds), Amp.Definition.Limits.FirstMessageTimeout);
        Assert.Equal(TimeSpan.FromSeconds(Limits.IdleSeconds), Amp.Definition.Limits.IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(Limits.PairingSeconds), PairProtocol.Definition.Limits.IdleTimeout);
    }

    // Damage: a codec that reused message objects and left a field of the previous frame behind would hand one peer's
    // values to the next message.
    [Fact]
    public void NoDataLeaksBetweenDecodedMessages()
    {
        MessageReuseContract.Verify(Amp.Definition, Frames.Encode(MessageSamples.Hello("a", "b")), Frames.Encode(MessageSamples.Hello()), MessageSamples.Describe);
        MessageReuseContract.Verify(Amp.Definition, Frames.Encode(new Have(MessageSamples.Hashes(3))), Frames.Encode(new Have([])), MessageSamples.Describe);
        MessageReuseContract.Verify(Amp.Definition, Frames.Encode(MessageSamples.Chunk(10)), Frames.Encode(MessageSamples.Chunk(3)), MessageSamples.Describe);
        MessageReuseContract.Verify(
            PairProtocol.Definition,
            Frames.Encode(PairProtocol.Definition, PairingSamples.AliceShows),
            Frames.Encode(PairProtocol.Definition, PairingSamples.NewDeviceShows),
            m => Convert.ToHexString(Frames.Encode(PairProtocol.Definition, m)));
    }

    // Damage: the frame header is the one thing a peer of another implementation reads before anything else; a little-
    // endian length, a length that leaves out the id, or a one-byte id breaks interoperation entirely (SPEC.md §5.1).
    [Fact]
    public void AFrameIsALengthCountingIdAndBodyThenAU16IdBigEndian()
    {
        byte[] ping = Frames.Encode(new Ping(0x0102030405060708));
        Assert.Equal(Convert.FromHexString("0000000A" + "000D" + "0102030405060708"), ping);

        byte[] bye = Frames.Encode(new Bye(ByeReason.Idle, "x"));
        Assert.Equal(Convert.FromHexString("00000006" + "000F" + "05" + "0001" + "78"), bye);

        byte[] pairCancel = Frames.Encode(PairProtocol.Definition, new PairCancel(PairCancelReason.Busy, string.Empty));
        Assert.Equal(Convert.FromHexString("00000005" + "0003" + "02" + "0000"), pairCancel);
    }
}
