using System;
using System.Security.Cryptography;

namespace Axiom.Mesh.Core;

/// <summary>
/// Makes a new P-256 key pair from the node's randomness (fundamentals plan §3.2: key creation is one of the places
/// randomness enters, and it enters through <see cref="IEntropy"/>): 32 bytes drawn until they are a valid private
/// scalar, 1 to n − 1, from which the platform derives the public point.
/// </summary>
internal static class KeyFactory
{
    // The order n of the P-256 group (SEC 2, §2.4.2), big-endian.
    private static ReadOnlySpan<byte> Order =>
    [
        0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xBC, 0xE6, 0xFA, 0xAD, 0xA7, 0x17, 0x9E, 0x84, 0xF3, 0xB9, 0xCA, 0xC2, 0xFC, 0x63, 0x25, 0x51,
    ];

    /// <summary>A new key pair as PKCS#8, which the caller owns and clears.</summary>
    /// <param name="entropy">The randomness.</param>
    /// <returns>The key.</returns>
    public static byte[] CreatePkcs8(IEntropy entropy)
    {
        ArgumentNullException.ThrowIfNull(entropy);
        byte[] scalar = new byte[32];
        try
        {
            do
                entropy.Fill(scalar);
            while (!IsScalar(scalar));

            using ECDsa key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = scalar });
            return key.ExportPkcs8PrivateKey();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(scalar);
        }
    }

    /// <summary>Whether 32 big-endian bytes are a private scalar of P-256: not zero, and below the group order.</summary>
    /// <param name="candidate">The bytes.</param>
    /// <returns><see langword="true"/> when 1 ≤ candidate &lt; n.</returns>
    public static bool IsScalar(ReadOnlySpan<byte> candidate)
    {
        if (candidate.Length != 32 || candidate.IndexOfAnyExcept((byte)0) < 0)
            return false;
        return candidate.SequenceCompareTo(Order) < 0;
    }
}
