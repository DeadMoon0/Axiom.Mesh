using System;

namespace Axiom.Mesh.Core;

/// <summary>
/// The order of verification of SPEC.md §10.6 as a pure function of a parsed object, the node's trust and what it
/// wants: Roster (needs its user known) → Manifest (self-verifying; needs its Package followed or listed) → Grant (needs
/// the followed Package's Manifest) → Entry (needs the writer's Roster and, for a grantee, the Grant;
/// <see cref="EntryAdmission"/> decides) → payload objects (need nothing but their hash; stored when wanted). An object
/// that arrives before what it needs is held; one whose signature fails against a key the node holds is faulty.
/// </summary>
internal static class IntakeRules
{
    /// <summary>Decides on one parsed object.</summary>
    /// <param name="parsed">The object, its hash proven and its form parsed.</param>
    /// <param name="trust">What the node trusts now.</param>
    /// <param name="wanted">What the node asked for.</param>
    /// <returns>Store, hold, drop or faulty.</returns>
    public static IntakeDecision Decide(ParsedObject parsed, TrustSnapshot trust, IWantedSet wanted)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(wanted);
        return parsed switch
        {
            UnverifiedRoster roster => Roster(roster, trust),
            UnverifiedManifest manifest => Manifest(manifest, trust),
            UnverifiedGrant grant => Grant(grant, trust),
            UnverifiedEntry entry => Entry(entry, trust),
            StorableObject unsigned => wanted.IsWanted(unsigned.Hash)
                ? IntakeDecision.Store(unsigned)
                : IntakeDecision.Drop("An object nobody here wanted (SPEC.md §10.3)."),
            _ => throw new ArgumentException($"A parsed object of type {parsed.GetType().Name} is neither signed nor storable.", nameof(parsed)),
        };
    }

    private static IntakeDecision Roster(UnverifiedRoster roster, TrustSnapshot trust)
    {
        if (!VerifiedRoster.TryVerify(roster, out VerifiedRoster? verified))
            return IntakeDecision.Faulty("A Roster whose signature does not verify against its own user key (SPEC.md §8.8).");
        return trust.IsKnownUser(roster.Content.User)
            ? IntakeDecision.Store(verified)
            : IntakeDecision.Hold(HeldNeedKind.KnownUser, roster.Content.User.Value);
    }

    private static IntakeDecision Manifest(UnverifiedManifest manifest, TrustSnapshot trust)
    {
        if (!VerifiedManifest.TryVerify(manifest, out VerifiedManifest? verified))
            return IntakeDecision.Faulty("A Manifest whose signature does not verify against its own owner key (SPEC.md §8.7).");
        return trust.IsFollowedOrListed(manifest.Content.Package)
            ? IntakeDecision.Store(verified)
            : IntakeDecision.Hold(HeldNeedKind.FollowedOrListed, manifest.Content.Package.Value);
    }

    private static IntakeDecision Grant(UnverifiedGrant grant, TrustSnapshot trust)
    {
        if (!trust.TryGetManifest(grant.Content.Package, out VerifiedManifest? manifest))
            return IntakeDecision.Hold(HeldNeedKind.Manifest, grant.Content.Package.Value);
        return VerifiedGrant.TryVerify(grant, manifest, out VerifiedGrant? verified)
            ? IntakeDecision.Store(verified)
            : IntakeDecision.Faulty("A Grant whose signature does not verify against its Package's owner key (SPEC.md §8.9).");
    }

    private static IntakeDecision Entry(UnverifiedEntry entry, TrustSnapshot trust)
    {
        EntryContent content = entry.Content;
        if (!trust.TryGetManifest(content.Package, out _))
            return IntakeDecision.Hold(HeldNeedKind.Manifest, content.Package.Value);
        if (!trust.TryGetDevice(content.Device, out TrustedDevice device))
            return IntakeDecision.Hold(HeldNeedKind.WritersRoster, content.Device.Value);
        if (!VerifiedEntry.TryVerify(entry, device.Key, out VerifiedEntry? verified))
            return IntakeDecision.Faulty("An Entry whose signature does not verify against its device's key (SPEC.md §8.6).");

        return EntryAdmission.Decide(verified, trust) switch
        {
            AdmissionDecision.Admit admit => IntakeDecision.Store(admit.Entry),
            AdmissionDecision.Unverifiable { Need: Prerequisite.WritersGrant } waiting => IntakeDecision.Hold(HeldNeedKind.WritersGrant, waiting.Writer.Value),
            AdmissionDecision.Unverifiable { Need: Prerequisite.Manifest } => IntakeDecision.Hold(HeldNeedKind.Manifest, content.Package.Value),
            AdmissionDecision.Unverifiable => IntakeDecision.Hold(HeldNeedKind.WritersRoster, content.Device.Value),
            AdmissionDecision.Invalid invalid => IntakeDecision.Faulty($"An invalid Entry: {invalid.Reason} (SPEC.md §8.6, §9.6)."),
            _ => throw new InvalidOperationException("EntryAdmission decided something it does not define."),
        };
    }
}
