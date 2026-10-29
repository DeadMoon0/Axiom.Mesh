using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §9.4: which Redactions withdraw which Entries.</summary>
public sealed class RedactionTests
{
    // §9.4: "A Redaction applies to a target of the same packageId whenever both are stored, whatever their order; a
    // Redaction whose target is in another Package, or is itself a Redaction, is ignored. It is honoured when its deviceId
    // equals the target's deviceId, or when its deviceId is a device of the owner; otherwise ignored."
    [Fact]
    public void ARedactionIsHonouredOnlyFromTheTargetsAuthorOrAnOwnerDevice_NeverOnARedactionOrAcrossPackages()
    {
        PackageId channel = Samples.Channel().Content.Package;
        PackageId elsewhere = Samples.Channel(nonce: 2).Content.Package;
        DeviceId bob = TestKeys.BobDesktop.PublicKey.AsDeviceId();
        DeviceId alice = TestKeys.AliceLaptop.PublicKey.AsDeviceId();

        EntryFacts bobsMessage = Facts(channel, EntryKind.Message, bob, 1);

        // The author withdraws their own message; an owner device withdraws anyone's.
        Assert.True(Redactions.Honours(Facts(channel, EntryKind.Redaction, bob, 2), bobsMessage, redactionFromAnOwnerDevice: false));
        Assert.True(Redactions.Honours(Facts(channel, EntryKind.Redaction, alice, 2), bobsMessage, redactionFromAnOwnerDevice: true));

        // Another grantee's device may not.
        Assert.False(Redactions.Honours(Facts(channel, EntryKind.Redaction, TestKeys.EveDevice.PublicKey.AsDeviceId(), 2), bobsMessage, redactionFromAnOwnerDevice: false));

        // Never across Packages, never on a Redaction, and only from a Redaction.
        Assert.False(Redactions.Honours(Facts(elsewhere, EntryKind.Redaction, bob, 2), bobsMessage, redactionFromAnOwnerDevice: true));
        Assert.False(Redactions.Honours(Facts(channel, EntryKind.Redaction, bob, 3), Facts(channel, EntryKind.Redaction, bob, 2), redactionFromAnOwnerDevice: true));
        Assert.False(Redactions.Honours(Facts(channel, EntryKind.Message, bob, 2), bobsMessage, redactionFromAnOwnerDevice: true));

        // Whatever their order: a Redaction with a lower clock than its target is honoured too.
        Assert.True(Redactions.Honours(Facts(channel, EntryKind.Redaction, bob, 0), bobsMessage, redactionFromAnOwnerDevice: false));
    }

    private static EntryFacts Facts(PackageId package, EntryKind kind, DeviceId device, ulong clock) =>
        new(package, kind, new EntryKey(clock, device, RawObjects.Hash((byte)(clock + (ulong)kind))));
}
