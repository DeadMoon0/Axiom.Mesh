using System;
using System.Numerics;

namespace Axiom.Mesh;

/// <summary>The constants of NIST P-256 (SEC 2 §2.4.2) that SPEC.md §2.2 and §2.3 need, and the two checks built on them.</summary>
internal static class P256
{
    /// <summary>The curve order <c>n</c>.</summary>
    public static Hash32 Order => new(0xFFFFFFFF00000000, 0xFFFFFFFFFFFFFFFF, 0xBCE6FAADA7179E84, 0xF3B9CAC2FC632551);

    /// <summary><c>(n − 1) / 2</c>, the largest low-s value (SPEC.md §2.2).</summary>
    public static Hash32 HalfOrder => new(0x7FFFFFFF80000000, 0x7FFFFFFFFFFFFFFF, 0xDE737D56D38BCF42, 0x79DCE5617E3192A8);

    // The field prime p and the coefficient b of y² = x³ − 3x + b.
    private static Hash32 Prime => new(0xFFFFFFFF00000001, 0x0000000000000000, 0x00000000FFFFFFFF, 0xFFFFFFFFFFFFFFFF);

    private static Hash32 B => new(0x5AC635D8AA3A93E7, 0xB3EBBD55769886BC, 0x651D06B0CC53B0F6, 0x3BCE3C3E27D2604B);

    /// <summary><c>n − value</c>, for a value in <c>[1, n − 1]</c>: the high-s twin of a low-s value and back.</summary>
    /// <param name="value">A value below the order.</param>
    /// <returns>The difference.</returns>
    public static Hash32 OrderMinus(Hash32 value)
    {
        Hash32 n = Order;
        ulong d3 = Subtract(n.W3, value.W3, 0, out ulong borrow);
        ulong d2 = Subtract(n.W2, value.W2, borrow, out borrow);
        ulong d1 = Subtract(n.W1, value.W1, borrow, out borrow);
        ulong d0 = Subtract(n.W0, value.W0, borrow, out _);
        return new Hash32(d0, d1, d2, d3);
    }

    /// <summary>Whether the 65-byte encoding <c>0x04 ‖ X ‖ Y</c> is a point on the curve (SPEC.md §2.3).</summary>
    /// <param name="encoded">The encoding.</param>
    /// <returns><see langword="true"/> for a point on P-256; the point at infinity has no such encoding.</returns>
    public static bool IsOnCurve(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length != 65 || encoded[0] != 0x04)
            return false;

        Hash32 x = new(encoded.Slice(1, 32));
        Hash32 y = new(encoded.Slice(33, 32));
        Hash32 prime = Prime;
        if (x >= prime || y >= prime)
            return false;

        BigInteger p = Big(prime);
        BigInteger bx = Big(x);
        BigInteger by = Big(y);
        BigInteger left = by * by % p;
        BigInteger right = ((bx * bx % p * bx) - (3 * bx) + Big(B)) % p;
        if (right.Sign < 0)
            right += p;
        return left == right;
    }

    private static BigInteger Big(Hash32 value)
    {
        Span<byte> bytes = stackalloc byte[Hash32.Length];
        value.CopyTo(bytes);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }

    private static ulong Subtract(ulong left, ulong right, ulong borrowIn, out ulong borrowOut)
    {
        ulong difference = left - right - borrowIn;
        borrowOut = (left < right || left - right < borrowIn) ? 1UL : 0UL;
        return difference;
    }
}
