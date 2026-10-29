using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>SPEC.md §8.6, §9.6 and §10.6: which verified Entries become storable.</summary>
public sealed class AdmissionTests
{
    // §8.6: "An Entry is valid when, at the time it is first verified, its signature verifies against the public key of
    // deviceId in the current roster of a user who is the Package's owner or holds a current Write Grant, and deviceId is
    // not removed in that roster. Once an Entry is stored it stays valid." §9.6: "A Roster or Grant change affects only
    // Entries not yet stored."
    [Fact]
    public void AnEntryIsValidOnlyFromADeviceOfTheOwnerOrAWriteGrantee_AndStaysValidAfterItsDeviceIsRemoved()
    {
        VerifiedManifest manifest = Samples.Channel();
        PackageId package = manifest.Content.Package;
        TrustView trust = new TrustView()
            .With(manifest)
            .With(Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false), (TestKeys.AlicePhone, false)))
            .With(Samples.Roster(TestKeys.Bob, 1, (TestKeys.BobDesktop, false)))
            .With(Samples.Roster(TestKeys.Eve, 1, (TestKeys.EveDevice, false)));

        // The owner's device.
        AdmittedEntry first = Admitted(EntryAdmission.Decide(Samples.Message(TestKeys.AliceLaptop, package, 0, EntryParents.None), trust));

        // A grantee: without a Grant the Entry waits for one; with Read it is invalid; with Write it is valid.
        VerifiedEntry fromBob = Samples.Message(TestKeys.BobDesktop, package, 1, EntryParents.Of(first.Hash));
        var waiting = Assert.IsType<AdmissionDecision.Unverifiable>(EntryAdmission.Decide(fromBob, trust));
        Assert.Equal(Prerequisite.WritersGrant, waiting.Need);
        Assert.Equal(TestKeys.Bob.PublicKey.AsUserId(), waiting.Writer);

        trust.With(Samples.Grant(manifest, TestKeys.Bob, GrantRights.Read));
        Assert.Equal(AdmissionRefusal.NotAWriter, Assert.IsType<AdmissionDecision.Invalid>(EntryAdmission.Decide(fromBob, trust)).Reason);

        trust.With(Samples.Grant(manifest, TestKeys.Bob, GrantRights.Write, seq: 2));
        Admitted(EntryAdmission.Decide(fromBob, trust));

        // A revoked grantee writes nothing valid any more.
        trust.With(Samples.Grant(manifest, TestKeys.Bob, GrantRights.None, seq: 3));
        Assert.Equal(AdmissionRefusal.NotAWriter, Assert.IsType<AdmissionDecision.Invalid>(EntryAdmission.Decide(fromBob, trust)).Reason);

        // The laptop is removed: a new Entry of it is invalid, the stored one stays valid and usable as a parent.
        trust.With(Samples.Roster(TestKeys.Alice, 2, (TestKeys.AliceLaptop, true), (TestKeys.AlicePhone, false)));
        VerifiedEntry late = Samples.Message(TestKeys.AliceLaptop, package, 1, EntryParents.Of(first.Hash));
        Assert.Equal(AdmissionRefusal.DeviceRemoved, Assert.IsType<AdmissionDecision.Invalid>(EntryAdmission.Decide(late, trust)).Reason);

        AdmittedEntry child = Admitted(EntryAdmission.Decide(Samples.Message(TestKeys.AlicePhone, package, 1, EntryParents.Of(first.Hash)), trust));
        Assert.False(Lamport.TryFindParentFault(child.Content, [first.Content.Facts], out _));
        Assert.Equal(TestKeys.AliceLaptop.PublicKey.AsDeviceId(), first.Content.Device);
    }

    // §10.6: "Entry (needs the writer's Roster and, for a grantee, the Grant)"; "Grant (needs the Manifest …)". What is
    // missing is said, so the Entry is held and decided again when it arrives.
    [Fact]
    public void AnEntryWaitsForItsManifestAndItsWritersRoster()
    {
        VerifiedManifest manifest = Samples.Channel();
        VerifiedEntry entry = Samples.Message(TestKeys.AliceLaptop, manifest.Content.Package, 0, EntryParents.None);
        TrustView trust = new();

        Assert.Equal(Prerequisite.Manifest, Assert.IsType<AdmissionDecision.Unverifiable>(EntryAdmission.Decide(entry, trust)).Need);
        trust.With(manifest);
        Assert.Equal(Prerequisite.WritersRoster, Assert.IsType<AdmissionDecision.Unverifiable>(EntryAdmission.Decide(entry, trust)).Need);
        trust.With(Samples.Roster(TestKeys.Alice, 1, (TestKeys.AliceLaptop, false)));
        Admitted(EntryAdmission.Decide(entry, trust));
    }

    private static AdmittedEntry Admitted(AdmissionDecision decision) => Assert.IsType<AdmissionDecision.Admit>(decision).Entry;
}
