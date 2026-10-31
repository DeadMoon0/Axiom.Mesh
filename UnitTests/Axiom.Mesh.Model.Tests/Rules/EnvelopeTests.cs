using System.Linq;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>
/// SPEC.md §8.5 and §8.6 (draft 5): an Entry's envelope is what a node reads; its payload is opaque, of any kind.
/// </summary>
public sealed class EnvelopeTests
{
    private static readonly PackageId Package = Samples.Channel().Content.Package;
    private static readonly DeviceId Laptop = TestKeys.AliceLaptop.PublicKey.AsDeviceId();

    // §8.6: "references: list<hash>(64), strictly ascending as byte strings (a repeat is malformed)".
    [Fact]
    public void ReferencesAreStrictlyAscending_ARepeatOrADescentIsMalformed()
    {
        UnverifiedEntry ascending = Samples.Parse<UnverifiedEntry>(Entry(500, [RawObjects.Hash(1), RawObjects.Hash(2)], default, [9]));
        Assert.Equal(new[] { RawObjects.Hash(1), RawObjects.Hash(2) }, ascending.Content.References);
        Assert.True(ascending.Content.References.Contains(RawObjects.Hash(2)));
        Assert.False(ascending.Content.References.Contains(RawObjects.Hash(3)));

        Samples.Fault(Entry(500, [RawObjects.Hash(1), RawObjects.Hash(1)], default, [9]));
        Samples.Fault(Entry(500, [RawObjects.Hash(2), RawObjects.Hash(1)], default, [9]));
        Assert.Empty(Samples.Parse<UnverifiedEntry>(Entry(500, [], default, [9])).Content.References);
    }

    // §8.5 (draft 5): "payloads are opaque to a node"; the sentence "a known kind that does not parse is malformed" is
    // gone: a Version, Message or Redaction whose payload does not parse is a well-formed Entry, stored and ordered like
    // any other.
    [Fact]
    public void APayloadOfAnyKindIsOpaque_AVersionMessageOrRedactionThatDoesNotParseIsWellFormed()
    {
        foreach (ushort kind in new ushort[] { 0, 1, 2, 3, 500, ushort.MaxValue })
        {
            UnverifiedEntry entry = Samples.Parse<UnverifiedEntry>(Entry(kind, [], default, [0xff, 0x00, 0x13]));
            Assert.Equal((EntryKind)kind, entry.Content.Kind);
            Assert.Equal(new byte[] { 0xff, 0x00, 0x13 }, entry.Content.Payload.ToArray());
            Assert.False(entry.Content.IsRedaction);
        }
    }

    // §8.6, §2.2: the signature covers every byte before it, references and redacts included: a writer cannot have a
    // relay add, drop or retarget either.
    [Fact]
    public void ReferencesAndRedactsAreSigned()
    {
        var body = new EntryBody((EntryKind)500, new byte[] { 1 }, EntryReferences.Of([RawObjects.Hash(4)]), RawObjects.Hash(6));
        VerifiedEntry entry = new EntryDraft { Package = Package, Clock = 0, Parents = EntryParents.None, Body = body }.Sign(TestKeys.AliceLaptop);
        byte[] bytes = entry.Bytes.ToArray();

        // The reference sits after tag, version, packageId, deviceId, clock, the empty parents, kind and the count.
        int reference = 2 + 32 + 32 + 8 + 2 + 2 + 2;
        int redacts = reference + 32;
        foreach (int at in new[] { reference + 31, redacts + 31 })
        {
            byte[] flipped = [.. bytes];
            flipped[at] ^= 1;
            UnverifiedEntry changed = Samples.Parse<UnverifiedEntry>(flipped);
            Assert.False(VerifiedEntry.TryVerify(changed, TestKeys.AliceLaptop.PublicKey, out _));
        }

        Assert.True(VerifiedEntry.TryVerify(Samples.Parse<UnverifiedEntry>(bytes), TestKeys.AliceLaptop.PublicKey, out _));
    }

    // §8.6 through the producer: a set of references in any order, with repeats, is signed sorted and once each, so a
    // signer cannot produce a malformed list; a list stated on the wire must already be canonical (LOCAL-SPEC.md §9.4).
    [Fact]
    public void TheDraftSortsReferencesAndKeepsEachOnce_AStatedListMustAlreadyBeCanonical()
    {
        EntryReferences references = EntryReferences.Of([RawObjects.Hash(9), RawObjects.Hash(2), RawObjects.Hash(9)]);
        Assert.Equal(new[] { RawObjects.Hash(2), RawObjects.Hash(9) }, references);

        var body = new EntryBody(EntryKind.Message, new byte[] { 1 }, references, default);
        VerifiedEntry entry = new EntryDraft { Package = Package, Clock = 0, Parents = EntryParents.None, Body = body }.Sign(TestKeys.AliceLaptop);
        Assert.Equal(new[] { RawObjects.Hash(2), RawObjects.Hash(9) }, entry.Content.References);

        Assert.True(EntryReferences.TryFromAscending([RawObjects.Hash(2), RawObjects.Hash(9)], out _));
        Assert.False(EntryReferences.TryFromAscending([RawObjects.Hash(9), RawObjects.Hash(2)], out _));
        Assert.False(EntryReferences.TryFromAscending([RawObjects.Hash(2), RawObjects.Hash(2)], out _));
        Assert.False(EntryReferences.TryFromAscending([.. Enumerable.Range(1, Limits.MaxEntryReferences + 1).Select(i => RawObjects.Hash((byte)i))], out _));
    }

    private static byte[] Entry(ushort kind, ObjectHash[] references, ObjectHash redacts, byte[] payload) =>
        RawObjects.FakeSigned(ObjectTag.Entry, RawObjects.EntryBody(Package, Laptop, 0, [], kind, references, redacts, payload));
}
