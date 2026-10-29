using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// An ECDSA P-256 signature in the 64-byte IEEE P1363 form <c>r ‖ s</c> (SPEC.md §2.2), always within
/// <c>1 ≤ r &lt; n</c> and <c>1 ≤ s ≤ (n − 1) / 2</c> (low-s).
/// </summary>
/// <remarks>
/// A value of this type is low-s by construction: <see cref="TryParse"/> refuses anything else, as SPEC.md §2.2 makes
/// such a signature malformed, and <see cref="Normalize"/> turns a signer's high-s output into its low-s twin. So one
/// signed content has one valid signature encoding, and a reader cannot mint a second name for it (§13). Only
/// <c>default</c>, with <c>r = s = 0</c>, is outside the bounds; it verifies against no key.
/// </remarks>
[ClosedShape("A P-256 signature is 64 bytes for ever (SPEC.md §2.2).")]
public readonly record struct Signature64
{
    /// <summary>The number of bytes, 64.</summary>
    public const int Length = 64;

    private Signature64(Hash32 r, Hash32 s)
    {
        R = r;
        S = s;
    }

    /// <summary>The first half, <c>r</c>.</summary>
    public Hash32 R { get; }

    /// <summary>The second half, <c>s</c>, never above <c>(n − 1) / 2</c>.</summary>
    public Hash32 S { get; }

    /// <summary>Parses 64 bytes that already are within the bounds of SPEC.md §2.2.</summary>
    /// <param name="bytes">The 64 bytes <c>r ‖ s</c>.</param>
    /// <param name="signature">The signature, or <c>default</c>.</param>
    /// <returns><see langword="false"/> for any other length, for <c>r</c> outside <c>[1, n)</c> or a high <c>s</c>.</returns>
    public static bool TryParse(ReadOnlySpan<byte> bytes, out Signature64 signature)
    {
        signature = default;
        if (bytes.Length != Length)
            return false;

        Hash32 r = new(bytes[..Hash32.Length]);
        Hash32 s = new(bytes[Hash32.Length..]);
        if (r.IsZero || r >= P256.Order || s.IsZero || s > P256.HalfOrder)
            return false;

        signature = new Signature64(r, s);
        return true;
    }

    /// <summary>
    /// The low-s form of a signature a signer produced: <c>s</c> is replaced by <c>n − s</c> when above
    /// <c>(n − 1) / 2</c>, which verifies against the same key and digest.
    /// </summary>
    /// <param name="p1363">The signer's 64 bytes <c>r ‖ s</c>.</param>
    /// <returns>The signature.</returns>
    /// <exception cref="ArgumentException">The bytes are not 64 long, or <c>r</c> or <c>s</c> is outside <c>[1, n)</c>: no signer produces that.</exception>
    public static Signature64 Normalize(ReadOnlySpan<byte> p1363)
    {
        if (p1363.Length != Length)
            throw new ArgumentException($"An IEEE P1363 P-256 signature is {Length} bytes; {p1363.Length} were given.", nameof(p1363));

        Hash32 r = new(p1363[..Hash32.Length]);
        Hash32 s = new(p1363[Hash32.Length..]);
        if (r.IsZero || r >= P256.Order || s.IsZero || s >= P256.Order)
            throw new ArgumentException("The signature's r or s is outside [1, n); it was not produced by an ECDSA P-256 signer.", nameof(p1363));

        return new Signature64(r, s > P256.HalfOrder ? P256.OrderMinus(s) : s);
    }

    /// <summary>Reads a <c>signature</c> (SPEC.md §5.2); outside the bounds of §2.2 the reader fails. Check <c>reader.Ok</c>.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The signature, or <c>default</c>.</returns>
    public static Signature64 Read(ref WireReader reader)
    {
        ReadOnlyMemory<byte> bytes = reader.BytesView(Length);
        if (!reader.Ok)
            return default;
        if (TryParse(bytes.Span, out Signature64 signature))
            return signature;

        reader.Fail();
        return default;
    }

    /// <summary>Writes the 64 bytes.</summary>
    /// <param name="writer">The writer.</param>
    public void Write(ref WireWriter writer)
    {
        Span<byte> bytes = stackalloc byte[Length];
        CopyTo(bytes);
        writer.Bytes(bytes);
    }

    /// <summary>Copies <c>r ‖ s</c> to the start of <paramref name="destination"/>.</summary>
    /// <param name="destination">At least 64 bytes.</param>
    public void CopyTo(Span<byte> destination)
    {
        R.CopyTo(destination);
        S.CopyTo(destination[Hash32.Length..]);
    }

    /// <inheritdoc />
    public override string ToString() => R.ToHex() + S.ToHex();
}
