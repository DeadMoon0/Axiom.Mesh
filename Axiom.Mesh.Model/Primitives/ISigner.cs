namespace Axiom.Mesh;

/// <summary>
/// The one seam a private key enters through: whatever holds a key (a key store, a test) signs digests and says
/// which public key verifies them. Model never sees the private key.
/// </summary>
public interface ISigner
{
    /// <summary>The public key that verifies this signer's signatures.</summary>
    PublicKey PublicKey { get; }

    /// <summary>Signs a digest; the result verifies against <see cref="PublicKey"/>.</summary>
    /// <param name="digest">The digest, <c>H(domain ‖ body)</c>.</param>
    /// <returns>The signature, low-s by its type (SPEC.md §2.2).</returns>
    Signature64 Sign(SigningDigest digest);
}
