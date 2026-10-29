using System.Diagnostics.CodeAnalysis;

namespace Axiom.Mesh;

/// <summary>
/// What the node trusts at one moment, as <see cref="EntryAdmission"/> reads it: current Manifests, current Grants, and
/// the devices of current Rosters. The engine's immutable trust snapshot implements it; tests hand in dictionaries.
/// </summary>
public interface ITrustSnapshotView
{
    /// <summary>The current Manifest of a Package the node follows.</summary>
    /// <param name="package">The Package.</param>
    /// <param name="manifest">The Manifest, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when one is held.</returns>
    bool TryGetManifest(PackageId package, [NotNullWhen(true)] out VerifiedManifest? manifest);

    /// <summary>The current Grant of a Package to a user.</summary>
    /// <param name="package">The Package.</param>
    /// <param name="grantee">The grantee.</param>
    /// <param name="grant">The Grant, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when one is held.</returns>
    bool TryGetGrant(PackageId package, UserId grantee, [NotNullWhen(true)] out VerifiedGrant? grant);

    /// <summary>A device of a known user's current Roster, with its removal decided by <see cref="RosterRules"/>.</summary>
    /// <param name="device">The device id.</param>
    /// <param name="trusted">The device, or <c>default</c>.</param>
    /// <returns><see langword="true"/> when a current Roster lists it.</returns>
    bool TryGetDevice(DeviceId device, out TrustedDevice trusted);
}
