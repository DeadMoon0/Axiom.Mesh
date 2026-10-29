using System;
using System.Security.Cryptography;

namespace Axiom.Mesh;

/// <summary>
/// An <see cref="ISigner"/> over a P-256 private key held by the BCL's <see cref="ECDsa"/>: IEEE P1363 output,
/// normalised to low-s (SPEC.md §2.2). Thread-safe.
/// </summary>
/// <remarks>
/// The BCL draws a fresh nonce per signature, so signing the same bytes twice gives two valid signatures, not one
/// (STRUCTURAL-DEBT.md entry 1). Both verify, and both are low-s.
/// </remarks>
public sealed class EcdsaSigner : ISigner, IDisposable
{
    private readonly ECDsa key;
    private readonly object gate = new();

    private EcdsaSigner(ECDsa key, PublicKey publicKey)
    {
        this.key = key;
        PublicKey = publicKey;
    }

    /// <inheritdoc />
    public PublicKey PublicKey { get; }

    /// <summary>A signer over a PKCS#8 (RFC 5208) P-256 private key.</summary>
    /// <param name="pkcs8">The DER encoding of the key.</param>
    /// <returns>The signer; dispose it to release the key.</returns>
    /// <exception cref="ArgumentException">The bytes are not a PKCS#8 P-256 private key.</exception>
    public static EcdsaSigner FromPkcs8(ReadOnlySpan<byte> pkcs8)
    {
        ECDsa key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out int read);
            ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
            if (read != pkcs8.Length || key.KeySize != 256 || parameters.Q.X?.Length != 32 || parameters.Q.Y?.Length != 32)
                throw new ArgumentException("The key is not a 256-bit EC key, or bytes follow it; AMP/1 signs with P-256 only (SPEC.md §2.2).", nameof(pkcs8));

            // Another 256-bit curve's point does not satisfy P-256's equation, so this also proves the curve.
            Span<byte> point = stackalloc byte[PublicKey.Length];
            point[0] = 0x04;
            parameters.Q.X.CopyTo(point[1..]);
            parameters.Q.Y.CopyTo(point[33..]);
            if (!PublicKey.TryParse(point, out PublicKey? publicKey))
                throw new ArgumentException("The key's public point is not on P-256; AMP/1 signs with P-256 only (SPEC.md §2.2).", nameof(pkcs8));

            return new EcdsaSigner(key, publicKey);
        }
        catch (CryptographicException ex)
        {
            key.Dispose();
            throw new ArgumentException("The bytes are not a PKCS#8 private key.", nameof(pkcs8), ex);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public Signature64 Sign(SigningDigest digest)
    {
        Span<byte> hash = stackalloc byte[Hash32.Length];
        digest.Value.CopyTo(hash);
        Span<byte> signature = stackalloc byte[Signature64.Length];
        lock (gate)
        {
            if (!key.TrySignHash(hash, signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation, out int written) || written != Signature64.Length)
                throw new InvalidOperationException("The ECDSA key produced no 64-byte P1363 signature.");
        }

        return Signature64.Normalize(signature);
    }

    /// <inheritdoc />
    public void Dispose() => key.Dispose();
}
