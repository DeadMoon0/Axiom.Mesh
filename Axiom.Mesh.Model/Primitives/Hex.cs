using System;

namespace Axiom.Mesh;

/// <summary>Lowercase hex (SPEC.md §1.3) without the allocations of the BCL's uppercase helpers on net8.0.</summary>
internal static class Hex
{
    private const string Digits = "0123456789abcdef";

    /// <summary>The bytes as lowercase hex.</summary>
    /// <param name="bytes">At most 128 bytes; ids, hashes, keys and signatures.</param>
    /// <returns>Two characters per byte.</returns>
    public static string Lower(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = bytes.Length <= 128 ? stackalloc char[bytes.Length * 2] : new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[2 * i] = Digits[bytes[i] >> 4];
            chars[(2 * i) + 1] = Digits[bytes[i] & 0xF];
        }

        return new string(chars);
    }

    /// <summary>Decodes exactly <c>destination.Length</c> bytes of hex, either case.</summary>
    /// <param name="hex">Two characters per byte.</param>
    /// <param name="destination">Receives the bytes.</param>
    /// <returns><see langword="true"/> when the text had the right length and only hex digits.</returns>
    public static bool TryDecode(ReadOnlySpan<char> hex, Span<byte> destination)
    {
        if (hex.Length != destination.Length * 2)
            return false;

        for (int i = 0; i < destination.Length; i++)
        {
            int high = Digit(hex[2 * i]);
            int low = Digit(hex[(2 * i) + 1]);
            if ((high | low) < 0)
                return false;
            destination[i] = (byte)((high << 4) | low);
        }

        return true;
    }

    private static int Digit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };
}
