using System;
using System.Buffers;
using System.Linq;
using Axiom.Mesh.Payloads;
using Axiom.Mesh.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>
/// PAYLOADS.md: the layouts of the Entry kinds of SPEC.md §14.7, which services parse themselves (a node never does,
/// SPEC.md §8.5), and how a payload fills the envelope fields a node acts on.
/// </summary>
public sealed class PayloadTests
{
    // PAYLOADS.md §1: "a payload of a kind with a layout here parses exactly to its end; a service treats one that does
    // not as its own fault". Any other kind is opaque.
    [Fact]
    public void ALayoutMustParseToItsEnd_AKindWithoutALayoutIsOpaque()
    {
        byte[] message = PayloadEncoder.Encode(new MessagePayload { Text = "hi", Attachments = [], ReplyTo = default });
        Assert.True(EntryPayload.TryParse(EntryKind.Message, message, out _));
        Assert.False(EntryPayload.TryParse(EntryKind.Message, (byte[])[.. message, 0], out _));
        Assert.False(EntryPayload.TryParse(EntryKind.Message, message[..^1], out _));
        Assert.False(EntryPayload.TryParse(EntryKind.Version, message, out _));
        Assert.False(EntryPayload.TryParse(EntryKind.Redaction, new byte[] { 1, 2, 3 }, out _));

        Assert.True(EntryPayload.TryParse((EntryKind)500, new byte[] { 1, 2, 3 }, out EntryPayload? opaque));
        var payload = Assert.IsType<OpaquePayload>(opaque);
        Assert.Equal((EntryKind)500, payload.Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, payload.Bytes.ToArray());
    }

    // PAYLOADS.md §2: "label: the branch name, main by default; MUST NOT be empty".
    [Fact]
    public void AVersionWithAnEmptyLabelDoesNotParseAndCannotBeEncoded()
    {
        var empty = new VersionPayload { Tree = RawObjects.Hash(1), Label = string.Empty, Prerelease = string.Empty, Notes = string.Empty };
        Assert.Throws<ArgumentException>(() => PayloadEncoder.Encode(empty));

        byte[] main = PayloadEncoder.Encode(new VersionPayload { Tree = RawObjects.Hash(1), Label = "main", Prerelease = string.Empty, Notes = string.Empty });
        byte[] emptied = [.. main[..32], 0, 0, .. main[(32 + 2 + 4)..]];
        Assert.True(EntryPayload.TryParse(EntryKind.Version, main, out _));
        Assert.False(EntryPayload.TryParse(EntryKind.Version, emptied, out _));
    }

    // PAYLOADS.md §2 to §4 with SPEC.md §8.6: a Version references its Tree, a Message its attachments, a Redaction
    // nothing; a Redaction's target goes into the envelope's redacts, which is what a node acts on.
    [Fact]
    public void AVersionReferencesItsTree_AMessageItsAttachments_ARedactionRedactsItsTarget()
    {
        Attachment photo = new("photo.jpg", RawObjects.Hash(3), 10);
        Attachment notes = new("notes", RawObjects.Hash(4), 0);
        EntryPayload message = RoundTrip(new MessagePayload { Text = "look", Attachments = [notes, photo], ReplyTo = RawObjects.Hash(9) });
        EntryPayload version = RoundTrip(new VersionPayload { Tree = RawObjects.Hash(5), Label = "main", Prerelease = "beta.2", Notes = "n" });
        EntryPayload redaction = RoundTrip(new RedactionPayload { Target = RawObjects.Hash(6) });

        Assert.Equal(new[] { notes.Hash, photo.Hash }, message.References);
        Assert.Equal(new[] { RawObjects.Hash(5) }, version.References);
        Assert.Empty(redaction.References);
        Assert.Equal(RawObjects.Hash(9), Assert.IsType<MessagePayload>(message).ReplyTo);

        EntryBody messageBody = message.ToBody();
        Assert.Equal(new[] { photo.Hash, notes.Hash }.Order(), messageBody.References);
        Assert.False(messageBody.IsRedaction);
        Assert.Equal(new[] { RawObjects.Hash(5) }, version.ToBody().References);
        EntryBody redactionBody = redaction.ToBody();
        Assert.Equal(RawObjects.Hash(6), redactionBody.Redacts);
        Assert.Empty(redactionBody.References);
        Assert.Equal(EntryKind.Redaction, redactionBody.Kind);
    }

    // An opaque payload is for kinds without a layout; a kind of PAYLOADS.md always has its own type.
    [Fact]
    public void AKindWithALayoutCannotBeCarriedAsOpaque()
    {
        Assert.Throws<ArgumentException>(() => new OpaquePayload(EntryKind.Message, new byte[] { 1 }));
    }

    // PAYLOADS.md §2 and §3: every bound of a layout is accepted at the limit and refused one over, by the parser a
    // service calls; to a node these bytes are opaque.
    [Theory]
    // PAYLOADS.md §2: a Version's label is at most 64 bytes.
    [InlineData("version label", 64)]
    // PAYLOADS.md §2: a Version's pre-release tag is at most 64 bytes.
    [InlineData("version prerelease", 64)]
    // PAYLOADS.md §2: a Version's notes are at most 4 096 bytes.
    [InlineData("version notes", 4_096)]
    // PAYLOADS.md §3: a Message's text is at most 16 384 bytes.
    [InlineData("message text", 16_384)]
    // PAYLOADS.md §3: a Message has at most 64 attachments.
    [InlineData("message attachments", 64)]
    // PAYLOADS.md §3: an attachment's name is at most 255 bytes.
    [InlineData("attachment name", 255)]
    public void EveryLayoutBoundIsAcceptedAtTheLimitAndRefusedOneOver(string bound, int limit)
    {
        (EntryKind kind, byte[] at) = Build(bound, limit);
        (_, byte[] over) = Build(bound, limit + 1);
        Assert.True(EntryPayload.TryParse(kind, at, out _));
        Assert.False(EntryPayload.TryParse(kind, over, out _));
    }

    private static EntryPayload RoundTrip(EntryPayload payload)
    {
        Assert.True(EntryPayload.TryParse(payload.Kind, PayloadEncoder.Encode(payload), out EntryPayload? parsed));
        return parsed;
    }

    private static (EntryKind Kind, byte[] Bytes) Build(string bound, int n) => bound switch
    {
        "version label" => (EntryKind.Version, Payload((ref WireWriter w) => { RawObjects.Hash(1).Write(ref w); Text(ref w, n); Text(ref w, 0); Text(ref w, 0); })),
        "version prerelease" => (EntryKind.Version, Payload((ref WireWriter w) => { RawObjects.Hash(1).Write(ref w); Text(ref w, 4); Text(ref w, n); Text(ref w, 0); })),
        "version notes" => (EntryKind.Version, Payload((ref WireWriter w) => { RawObjects.Hash(1).Write(ref w); Text(ref w, 4); Text(ref w, 0); Text(ref w, n); })),
        "message text" => (EntryKind.Message, Message(n, 0, 1)),
        "message attachments" => (EntryKind.Message, Message(1, n, 1)),
        "attachment name" => (EntryKind.Message, Message(1, 1, n)),
        _ => throw new ArgumentException("Unknown bound " + bound, nameof(bound)),
    };

    private delegate void Fields(ref WireWriter writer);

    private static byte[] Payload(Fields fields)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new WireWriter(output);
        fields(ref writer);
        return output.WrittenSpan.ToArray();
    }

    // A string of n bytes of 'a' (ASCII is one byte per character).
    private static void Text(ref WireWriter writer, int n) => RawObjects.String(ref writer, Enumerable.Repeat((byte)'a', n).ToArray());

    private static byte[] Message(int text, int attachments, int nameLength) => Payload((ref WireWriter w) =>
    {
        Text(ref w, text);
        w.UInt16BigEndian((ushort)attachments);
        for (int i = 0; i < attachments; i++)
        {
            Text(ref w, nameLength);
            RawObjects.Hash(1).Write(ref w);
            w.UInt64BigEndian(0);
        }

        RawObjects.Hash(0).Write(ref w);
    });
}
