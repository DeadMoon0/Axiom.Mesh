using System;
using System.Linq;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §8.5: Entry payloads by kind.</summary>
public sealed class PayloadTests
{
    private static readonly PackageId Package = Samples.Channel().Content.Package;

    // §8.5: "A payload of a known kind that does not parse exactly to its end makes the Entry malformed. An unknown kind is
    // not malformed: the Entry is stored, replicated and ordered, and its payload is opaque."
    [Fact]
    public void AKnownKindsPayloadMustParseToItsEnd_AnUnknownKindIsOpaque()
    {
        byte[] message = PayloadEncoder.Encode(new MessagePayload { Text = "hi", Attachments = [], ReplyTo = default });
        Samples.Parse<UnverifiedEntry>(Entry(2, message));
        Samples.Fault(Entry(2, [.. message, 0]));
        Samples.Fault(Entry(2, message[..^1]));
        Samples.Fault(Entry(1, message));
        Samples.Fault(Entry(3, [1, 2, 3]));

        UnverifiedEntry opaque = Samples.Parse<UnverifiedEntry>(Entry(500, [1, 2, 3]));
        var payload = Assert.IsType<OpaquePayload>(opaque.Content.Payload);
        Assert.Equal((EntryKind)500, payload.Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, payload.Bytes.ToArray());
    }

    // §8.5: "label: the branch name, main by default; MUST NOT be empty".
    [Fact]
    public void AVersionWithAnEmptyLabelIsMalformedAndCannotBeEncoded()
    {
        var empty = new VersionPayload { Tree = RawObjects.Hash(1), Label = string.Empty, Prerelease = string.Empty, Notes = string.Empty };
        Assert.Throws<ArgumentException>(() => PayloadEncoder.Encode(empty));

        byte[] main = PayloadEncoder.Encode(new VersionPayload { Tree = RawObjects.Hash(1), Label = "main", Prerelease = string.Empty, Notes = string.Empty });
        byte[] emptied = [.. main[..32], 0, 0, .. main[(32 + 2 + 4)..]];
        Samples.Parse<UnverifiedEntry>(Entry(1, main));
        Samples.Fault(Entry(1, emptied));
    }

    // §8.5, LOCAL-SPEC.md §9.4: the objects a payload references are a Version's Tree and a Message's attachments; a
    // Redaction's target is an Entry, not a referenced object.
    [Fact]
    public void AVersionReferencesItsTree_AMessageItsAttachments_ARedactionNothing()
    {
        Attachment photo = new("photo.jpg", RawObjects.Hash(3), 10);
        Attachment notes = new("notes", RawObjects.Hash(4), 0);
        EntryPayload message = RoundTrip(new MessagePayload { Text = "look", Attachments = [photo, notes], ReplyTo = RawObjects.Hash(9) });
        EntryPayload version = RoundTrip(new VersionPayload { Tree = RawObjects.Hash(5), Label = "main", Prerelease = "beta.2", Notes = "n" });
        EntryPayload redaction = RoundTrip(new RedactionPayload { Target = RawObjects.Hash(6) });

        Assert.Equal(new[] { photo.Hash, notes.Hash }, message.References);
        Assert.Equal(new[] { RawObjects.Hash(5) }, version.References);
        Assert.Empty(redaction.References);
        Assert.Equal(RawObjects.Hash(9), Assert.IsType<MessagePayload>(message).ReplyTo);
        Assert.Equal(new[] { photo, notes }, Assert.IsType<MessagePayload>(message).Attachments.ToArray());
    }

    // An opaque payload is for extension kinds; a built-in kind always has its own layout (§8.5).
    [Fact]
    public void ABuiltInKindCannotBeCarriedAsOpaque()
    {
        Assert.Throws<ArgumentException>(() => new OpaquePayload(EntryKind.Message, new byte[] { 1 }));
    }

    private static EntryPayload RoundTrip(EntryPayload payload)
    {
        Assert.True(EntryPayload.TryParse(payload.Kind, PayloadEncoder.Encode(payload), out EntryPayload? parsed));
        return parsed;
    }

    private static byte[] Entry(ushort kind, byte[] payload) =>
        RawObjects.Signed(ObjectTag.Entry, SigningDomain.Entry, TestKeys.AliceLaptop, RawObjects.EntryBody(Package, TestKeys.AliceLaptop.PublicKey.AsDeviceId(), 0, [], kind, payload));
}
