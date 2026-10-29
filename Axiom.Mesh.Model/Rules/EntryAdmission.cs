using System;

namespace Axiom.Mesh;

/// <summary>
/// Whether a verified Entry is valid (SPEC.md §8.6, §9.6): its device is, in the node's current Rosters, a not-removed
/// device of the Package's owner or of a user holding a current <c>Write</c> Grant. The only way to a storable Entry.
/// </summary>
public static class EntryAdmission
{
    /// <summary>
    /// Decides on an Entry against what the node trusts now. The decision holds for the Entry once stored: later Rosters
    /// and Grants affect only Entries not yet stored (§9.6).
    /// </summary>
    /// <param name="entry">The Entry, verified against its device's key.</param>
    /// <param name="trust">The node's current trust.</param>
    /// <returns><see cref="AdmissionDecision.Admit"/>, <see cref="AdmissionDecision.Unverifiable"/> or <see cref="AdmissionDecision.Invalid"/>.</returns>
    public static AdmissionDecision Decide(VerifiedEntry entry, ITrustSnapshotView trust)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(trust);
        EntryContent content = entry.Content;

        if (!trust.TryGetManifest(content.Package, out VerifiedManifest? manifest))
            return new AdmissionDecision.Unverifiable(Prerequisite.Manifest, default);
        if (!trust.TryGetDevice(content.Device, out TrustedDevice device))
            return new AdmissionDecision.Unverifiable(Prerequisite.WritersRoster, default);
        if (device.IsRemoved)
            return new AdmissionDecision.Invalid(AdmissionRefusal.DeviceRemoved);

        if (device.User != manifest.Content.Owner)
        {
            if (!trust.TryGetGrant(content.Package, device.User, out VerifiedGrant? grant))
                return new AdmissionDecision.Unverifiable(Prerequisite.WritersGrant, device.User);
            if (grant.Content.Rights != GrantRights.Write)
                return new AdmissionDecision.Invalid(AdmissionRefusal.NotAWriter);
        }

        return new AdmissionDecision.Admit(entry.Admit());
    }
}
