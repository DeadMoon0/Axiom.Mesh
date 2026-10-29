using System;
using System.Diagnostics.CodeAnalysis;

namespace Axiom.Mesh;

/// <summary>
/// A Grant whose signature verifies against the owner key of its Package's current Manifest (SPEC.md §8.9): storable.
/// Whether it is current is <see cref="CurrentObject"/>'s decision.
/// </summary>
public sealed class VerifiedGrant : StorableObject
{
    private VerifiedGrant(UnverifiedGrant grant)
        : base(grant.Hash, grant.Bytes) => Content = grant.Content;

    /// <summary>What the bytes say; signed by the Package's owner.</summary>
    public GrantContent Content { get; }

    /// <inheritdoc />
    public override ObjectTag Tag => ObjectTag.Grant;

    /// <summary>
    /// Checks the Grant's signature over the bytes received against the owner key of <paramref name="manifest"/>, with
    /// domain <c>axiom-mesh/grant/1</c> (SPEC.md §2.2, §8.9).
    /// </summary>
    /// <param name="grant">The parsed Grant.</param>
    /// <param name="manifest">The current Manifest of the Grant's Package.</param>
    /// <param name="verified">The verified Grant, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the signature does not verify: the Grant is invalid.</returns>
    /// <exception cref="ArgumentException"><paramref name="manifest"/> is another Package's.</exception>
    public static bool TryVerify(UnverifiedGrant grant, VerifiedManifest manifest, [NotNullWhen(true)] out VerifiedGrant? verified)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Content.Package != grant.Content.Package)
            throw new ArgumentException($"The Manifest is Package {manifest.Content.Package}'s, but the Grant is for {grant.Content.Package}.", nameof(manifest));

        bool valid = manifest.Content.OwnerKey.Verify(Digests.Of(SigningDomain.Grant, grant.SignedBytes), grant.Content.Signature);
        verified = valid ? new VerifiedGrant(grant) : null;
        return valid;
    }
}
