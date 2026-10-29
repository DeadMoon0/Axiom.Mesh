using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// The 32 random bytes chosen when a Package is created and carried in its Manifest (SPEC.md §2.3, §8.7), from which its id derives. Ordered as unsigned byte strings.
/// </summary>
/// <param name="Value">The 32 bytes.</param>
[ClosedShape("The nonce is bytes<32> (SPEC.md §8.7).")]
public readonly record struct PackageNonce(Hash32 Value) : IComparable<PackageNonce>
{
    /// <summary>Reads 32 bytes (SPEC.md §5.2). Check <c>reader.Ok</c>.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The value read.</returns>
    public static PackageNonce Read(ref WireReader reader) => new(Hash32.Read(ref reader));

    /// <summary>Parses 64 hex digits.</summary>
    /// <param name="hex">The text.</param>
    /// <param name="value">The value, or zero.</param>
    /// <returns><see langword="true"/> when the text was 64 hex digits.</returns>
    public static bool TryParseHex(ReadOnlySpan<char> hex, out PackageNonce value)
    {
        bool parsed = Hash32.TryParseHex(hex, out Hash32 bytes);
        value = new PackageNonce(bytes);
        return parsed;
    }

    /// <summary>Writes the 32 bytes.</summary>
    /// <param name="writer">The writer.</param>
    public void Write(ref WireWriter writer) => Value.Write(ref writer);

    /// <inheritdoc />
    public int CompareTo(PackageNonce other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => Value.ToHex();

    /// <summary>Whether <paramref name="left"/> sorts before <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator <(PackageNonce left, PackageNonce right) => left.Value < right.Value;

    /// <summary>Whether <paramref name="left"/> sorts after <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator >(PackageNonce left, PackageNonce right) => left.Value > right.Value;

    /// <summary>Whether <paramref name="left"/> sorts before or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator <=(PackageNonce left, PackageNonce right) => left.Value <= right.Value;

    /// <summary>Whether <paramref name="left"/> sorts after or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator >=(PackageNonce left, PackageNonce right) => left.Value >= right.Value;
}
