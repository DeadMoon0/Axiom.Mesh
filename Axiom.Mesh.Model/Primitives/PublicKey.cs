using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Threading;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// An ECDSA P-256 public key in its 65-byte uncompressed encoding <c>0x04 ‖ X ‖ Y</c> (SPEC.md §2.3), proven to be a
/// point on the curve when it was made: a value of this type is never malformed.
/// </summary>
/// <remarks>
/// Its id, <c>H(encoding)</c>, is computed once; the same key is a user key or a device key, never both (§2.3), and
/// which one is the caller's statement through <see cref="AsUserId"/> or <see cref="AsDeviceId"/>. Verification is
/// thread-safe; the BCL key behind it is created on the first <see cref="Verify"/>.
/// </remarks>
public sealed class PublicKey : IEquatable<PublicKey>
{
    /// <summary>The number of bytes of the encoding, 65.</summary>
    public const int Length = 65;

    private readonly byte[] encoded;
    private readonly Hash32 id;
    private readonly object gate = new();
    private ECDsa? verifier;

    private PublicKey(byte[] encoded)
    {
        this.encoded = encoded;
        id = Hash32.Of(encoded);
    }

    /// <summary>The 65 bytes <c>0x04 ‖ X ‖ Y</c>.</summary>
    public ReadOnlySpan<byte> Encoded => encoded;

    /// <summary>Parses a <c>pubkey</c>: 65 bytes, first byte <c>0x04</c>, a point on P-256 (SPEC.md §2.3).</summary>
    /// <param name="encoded">The encoding.</param>
    /// <param name="key">The key, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> for a malformed key.</returns>
    public static bool TryParse(ReadOnlySpan<byte> encoded, [NotNullWhen(true)] out PublicKey? key)
    {
        key = P256.IsOnCurve(encoded) ? new PublicKey(encoded.ToArray()) : null;
        return key is not null;
    }

    /// <summary>Reads a <c>pubkey</c> (SPEC.md §5.2); a malformed key fails the reader.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="key">The key, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a well-formed key was read.</returns>
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out PublicKey? key)
    {
        ReadOnlyMemory<byte> bytes = reader.BytesView(Length);
        if (reader.Ok && TryParse(bytes.Span, out key))
            return true;

        reader.Fail();
        key = null;
        return false;
    }

    /// <summary>The id of the user whose key this is: <c>H(encoding)</c> (SPEC.md §2.3).</summary>
    /// <returns>The user id.</returns>
    public UserId AsUserId() => new(id);

    /// <summary>The id of the device whose key this is: <c>H(encoding)</c> (SPEC.md §2.3).</summary>
    /// <returns>The device id.</returns>
    public DeviceId AsDeviceId() => new(id);

    /// <summary>Whether <paramref name="signature"/> is this key's signature over <paramref name="digest"/>.</summary>
    /// <param name="digest">The digest of the bytes received (SPEC.md §2.2).</param>
    /// <param name="signature">The signature, low-s by its type.</param>
    /// <returns><see langword="true"/> when it verifies.</returns>
    public bool Verify(SigningDigest digest, Signature64 signature)
    {
        Span<byte> hash = stackalloc byte[Hash32.Length];
        digest.Value.CopyTo(hash);
        Span<byte> p1363 = stackalloc byte[Signature64.Length];
        signature.CopyTo(p1363);

        ECDsa key = Volatile.Read(ref verifier) ?? CreateVerifier();
        lock (gate)
            return key.VerifyHash(hash, p1363, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>Writes the 65 bytes.</summary>
    /// <param name="writer">The writer.</param>
    public void Write(ref WireWriter writer) => writer.Bytes(encoded);

    /// <inheritdoc />
    public bool Equals(PublicKey? other) => other is not null && encoded.AsSpan().SequenceEqual(other.encoded);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as PublicKey);

    /// <inheritdoc />
    public override int GetHashCode() => id.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => Hex.Lower(encoded);

    private ECDsa CreateVerifier()
    {
        lock (gate)
        {
            if (verifier is not null)
                return verifier;

            ECParameters parameters = new()
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = encoded[1..33], Y = encoded[33..] },
            };
            ECDsa created = ECDsa.Create(parameters);
            Volatile.Write(ref verifier, created);
            return created;
        }
    }
}
