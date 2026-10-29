using System;
using System.Security.Cryptography;
using System.Text;

namespace Axiom.Mesh.Protocol;

/// <summary>
/// The short authentication string both users compare before they confirm a pairing (PAIR-SPEC.md §8, DESIGN.md §5.2).
/// </summary>
public static class Sas
{
    /// <summary>The domain the derivation hashes first.</summary>
    public const string Domain = "axiom-mesh/pair/1";

    /// <summary>The characters shown, without the hyphen: 8 base32 characters, 40 bits.</summary>
    public const int Characters = 8;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int BitsPerCharacter = 5;

    /// <summary>
    /// Derives the string: the first 8 characters of the RFC 4648 base32 encoding of
    /// <c>H("axiom-mesh/pair/1" ‖ sort(userKeyA, userKeyB) ‖ sort(tlsKeyA, tlsKeyB))</c>, each key its 65-byte encoding
    /// and an absent user key 65 zero bytes, each pair sorted as unsigned byte strings, shown as <c>XXXX-XXXX</c>.
    /// Both sides compute the same string whichever of them is A.
    /// </summary>
    /// <param name="userKeyA">One side's user key; <see langword="null"/> for a device being enrolled.</param>
    /// <param name="tlsKeyA">That side's TLS (device) key.</param>
    /// <param name="userKeyB">The other side's user key; <see langword="null"/> for a device being enrolled.</param>
    /// <param name="tlsKeyB">The other side's TLS (device) key.</param>
    /// <returns>The string, such as <c>K7QD-M2XA</c>.</returns>
    public static string Derive(PublicKey? userKeyA, PublicKey tlsKeyA, PublicKey? userKeyB, PublicKey tlsKeyB)
    {
        ArgumentNullException.ThrowIfNull(tlsKeyA);
        ArgumentNullException.ThrowIfNull(tlsKeyB);

        Span<byte> input = stackalloc byte[DomainLength + (4 * PublicKey.Length)];
        int written = Encoding.ASCII.GetBytes(Domain, input);
        written += WriteSorted(KeyOrZero(userKeyA), KeyOrZero(userKeyB), input[written..]);
        written += WriteSorted(tlsKeyA.Encoded, tlsKeyB.Encoded, input[written..]);

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input[..written], digest);
        return Format(digest);
    }

    private static int DomainLength => Domain.Length;

    private static ReadOnlySpan<byte> KeyOrZero(PublicKey? key) => key is null ? new byte[PublicKey.Length] : key.Encoded;

    private static int WriteSorted(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<byte> destination)
    {
        bool aFirst = a.SequenceCompareTo(b) <= 0;
        (aFirst ? a : b).CopyTo(destination);
        (aFirst ? b : a).CopyTo(destination[a.Length..]);
        return a.Length + b.Length;
    }

    // The first 40 bits, five per character, most significant first; a hyphen after the fourth character.
    private static string Format(ReadOnlySpan<byte> digest)
    {
        ulong bits = 0;
        for (int i = 0; i < Characters * BitsPerCharacter / 8; i++)
            bits = (bits << 8) | digest[i];

        Span<char> text = stackalloc char[Characters + 1];
        int position = 0;
        for (int i = 0; i < Characters; i++)
        {
            if (i == Characters / 2)
                text[position++] = '-';
            int shift = (Characters - 1 - i) * BitsPerCharacter;
            text[position++] = Base32Alphabet[(int)((bits >> shift) & 0x1F)];
        }

        return new string(text);
    }
}
