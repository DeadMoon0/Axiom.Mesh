using Axiom.Mesh.Protocol.Tests.Support;
using Axiom.ProtoStream;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Protocol.Tests.Rules;

/// <summary>Frames and message ids (SPEC.md §5.1, §1.4, §14.1).</summary>
public sealed class FrameTests
{
    // §5.1: "length MUST be at least 2 and at most 4 198 400 (4 MiB + 4 KiB). A frame outside these bounds is a
    // violation." The length counts the two id bytes and the payload.
    [Fact]
    public void ALengthOf1IsAViolation_2IsAccepted_4198401IsAViolation_4198400IsAccepted()
    {
        Assert.Equal(DecodeStatus.Invalid, Frames.Decode(Frames.WithLength(0, [])).Status);
        Assert.Equal(DecodeStatus.Invalid, Frames.Decode(Frames.WithLength(1, [0x00])).Status);

        DecodeOutcome<MeshMessage> two = Frames.Decode(Frames.WithLength(2, [0x00, (byte)MessageIds.FirstExtension]));
        Assert.Equal(DecodeStatus.Message, two.Status);
        Assert.Equal(MessageIds.FirstExtension, Assert.IsType<Extension>(two.Message).Id);

        byte[] largest = Frames.Raw(MessageIds.FirstExtension, new byte[4_198_400 - 2]);
        Assert.Equal(4_198_400 + 4, largest.Length);
        Assert.IsType<Extension>(Frames.Decode(largest).Message);

        // One byte more is refused from the length field alone, before any content arrives.
        DecodeOutcome<MeshMessage> over = Frames.Decode(Frames.WithLength(4_198_401, [0x00, (byte)MessageIds.FirstExtension]));
        Assert.Equal(DecodeStatus.Invalid, over.Status);
        Assert.Equal(ViolationCode.LimitExceeded, over.Code);
    }

    // §1.4: "a frame whose message id is 17 to 255 and unknown to the receiver MUST be ignored (read and discarded); an
    // unknown id 0 to 16 is a violation"; §14.1: "256 and above are never assigned".
    [Theory]
    [InlineData(0, false)] // never assigned
    [InlineData(17, true)] // the first extension id
    [InlineData(255, true)] // the last extension id
    [InlineData(256, false)] // the first id above the extension range
    [InlineData(65535, false)] // the largest u16
    public void AnUnknownIdBetween17And255IsIgnored_0OrAbove255IsAViolation(int id, bool ignored)
    {
        DecodeOutcome<MeshMessage> outcome = Frames.Decode(Frames.Raw((ushort)id, [1, 2, 3]));

        if (ignored)
        {
            Extension extension = Assert.IsType<Extension>(outcome.Message);
            Assert.Equal(id, extension.Id);
        }
        else
        {
            Assert.Equal(DecodeStatus.Invalid, outcome.Status);
        }
    }

    // §14.1 assigns 1 to 16; each decodes as its own message, never as an extension.
    [Fact]
    public void EveryAssignedIdDecodesAsItsOwnMessage()
    {
        foreach (MeshMessage message in MessageSamples.All())
        {
            if (message is Extension)
                continue;
            MeshMessage decoded = Frames.Decode(Frames.Encode(message)).Message!;
            Assert.Equal(message.GetType(), decoded.GetType());
            Assert.Equal(message.Id, decoded.Id);
        }
    }
}
