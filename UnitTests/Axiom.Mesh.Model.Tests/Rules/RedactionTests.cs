using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §9.4: which redactions withdraw which Entries, read from the envelope's <c>redacts</c>.</summary>
public sealed class RedactionTests
{
    private static readonly PackageId Channel = Samples.Channel().Content.Package;
    private static readonly PackageId Elsewhere = Samples.Channel(nonce: 2).Content.Package;
    private static readonly DeviceId Bob = TestKeys.BobDesktop.PublicKey.AsDeviceId();
    private static readonly DeviceId Alice = TestKeys.AliceLaptop.PublicKey.AsDeviceId();

    // §9.4: "An Entry whose redacts is not zero is a redaction of that target, whatever its kind. A redaction applies to a
    // target of the same packageId whenever both are stored, whatever their order; a redaction whose target is in another
    // Package, or is itself a redaction, is ignored. It is honoured when its deviceId equals the target's deviceId, or
    // when its deviceId is a device of the owner; otherwise ignored."
    [Fact]
    public void ARedactionIsHonouredOnlyFromTheTargetsAuthorOrAnOwnerDevice_NeverOnARedactionOrAcrossPackages()
    {
        EntryFacts bobsMessage = Entry(Channel, Bob, 1, redacts: default);

        // The author withdraws their own message; an owner device withdraws anyone's.
        Assert.True(Redactions.Honours(Entry(Channel, Bob, 2, bobsMessage.Key.Hash), bobsMessage, redactionFromAnOwnerDevice: false));
        Assert.True(Redactions.Honours(Entry(Channel, Alice, 2, bobsMessage.Key.Hash), bobsMessage, redactionFromAnOwnerDevice: true));

        // Another grantee's device may not.
        Assert.False(Redactions.Honours(Entry(Channel, TestKeys.EveDevice.PublicKey.AsDeviceId(), 2, bobsMessage.Key.Hash), bobsMessage, redactionFromAnOwnerDevice: false));

        // Never across Packages, never on a redaction, and only from an Entry whose redacts names the target.
        EntryFacts redaction = Entry(Channel, Bob, 2, RawObjects.Hash(99));
        Assert.False(Redactions.Honours(Entry(Elsewhere, Bob, 2, bobsMessage.Key.Hash), bobsMessage, redactionFromAnOwnerDevice: true));
        Assert.False(Redactions.Honours(Entry(Channel, Bob, 3, redaction.Key.Hash), redaction, redactionFromAnOwnerDevice: true));
        Assert.False(Redactions.Honours(Entry(Channel, Bob, 2, redacts: default), bobsMessage, redactionFromAnOwnerDevice: true));
        Assert.False(Redactions.Honours(Entry(Channel, Bob, 2, RawObjects.Hash(98)), bobsMessage, redactionFromAnOwnerDevice: true));

        // Whatever their order: a redaction with a lower clock than its target is honoured too.
        Assert.True(Redactions.Honours(Entry(Channel, Bob, 0, bobsMessage.Key.Hash), bobsMessage, redactionFromAnOwnerDevice: false));
    }

    // §8.6: redacts is part of the signed envelope; an Entry of a kind no service registered still redacts by it (§9.4
    // "whatever its kind").
    [Fact]
    public void AnEntryOfAnyKindIsARedactionWhenItsRedactsIsNotZero()
    {
        var body = new EntryBody((EntryKind)500, new byte[] { 7 }, EntryReferences.None, RawObjects.Hash(5));
        VerifiedEntry entry = new EntryDraft { Package = Channel, Clock = 0, Parents = EntryParents.None, Body = body }.Sign(TestKeys.BobDesktop);
        UnverifiedEntry parsed = Samples.Parse<UnverifiedEntry>(entry.Bytes);

        Assert.True(parsed.Content.IsRedaction);
        Assert.Equal(RawObjects.Hash(5), parsed.Content.Redacts);
        Assert.Equal(RawObjects.Hash(5), parsed.Content.Facts.Redacts);
        Assert.True(Redactions.Honours(parsed.Content.Facts, new EntryFacts(Channel, default, new EntryKey(3, Bob, RawObjects.Hash(5))), redactionFromAnOwnerDevice: false));
    }

    private static EntryFacts Entry(PackageId package, DeviceId device, ulong clock, ObjectHash redacts) =>
        new(package, redacts, new EntryKey(clock, device, RawObjects.Hash((byte)(clock + (redacts.IsZero ? 0u : 50u)))));
}
