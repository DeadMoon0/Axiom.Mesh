using System;
using System.Diagnostics.CodeAnalysis;

namespace Axiom.Mesh;

/// <summary>
/// A Manifest whose signature verifies against its own owner key (SPEC.md §8.7): storable. Whether it is trusted
/// (its Package followed, its owner a known user) and whether it is current are the node's decisions
/// (§8.7, §10.6, <see cref="CurrentObject"/>).
/// </summary>
public sealed class VerifiedManifest : StorableObject
{
    private VerifiedManifest(UnverifiedManifest manifest)
        : base(manifest.Hash, manifest.Bytes) => Content = manifest.Content;

    /// <summary>What the bytes say; signed by the owner key it names.</summary>
    public ManifestContent Content { get; }

    /// <inheritdoc />
    public override ObjectTag Tag => ObjectTag.Manifest;

    /// <summary>
    /// Checks the Manifest's signature over the bytes received against its own <c>ownerKey</c>, with domain
    /// <c>axiom-mesh/manifest/1</c> (SPEC.md §2.2, §8.7).
    /// </summary>
    /// <param name="manifest">The parsed Manifest.</param>
    /// <param name="verified">The verified Manifest, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the signature does not verify: the Manifest is invalid.</returns>
    public static bool TryVerify(UnverifiedManifest manifest, [NotNullWhen(true)] out VerifiedManifest? verified)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        bool valid = manifest.Content.OwnerKey.Verify(Digests.Of(SigningDomain.Manifest, manifest.SignedBytes), manifest.Content.Signature);
        verified = valid ? new VerifiedManifest(manifest) : null;
        return valid;
    }
}
