using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// A Package's id: <c>H("axiom-mesh/package/1" ‖ owner user id ‖ nonce)</c> (SPEC.md §2.3), so that every receiver of
/// the Manifest can check that only the owner's key could have made it (§8.7, §13). Ordered as unsigned byte strings.
/// </summary>
/// <param name="Value">The 32 bytes.</param>
[ClosedShape("An id is one SHA-256 output (SPEC.md §2.3).")]
public readonly record struct PackageId(Hash32 Value) : IComparable<PackageId>
{
    /// <summary>The hash domain of the derivation, <c>axiom-mesh/package/1</c> (SPEC.md §14.5).</summary>
    public const string DerivationDomain = "axiom-mesh/package/1";

    private static ReadOnlySpan<byte> DomainUtf8 => "axiom-mesh/package/1"u8;

    /// <summary>The id of the Package <paramref name="owner"/> creates with <paramref name="nonce"/> (SPEC.md §2.3).</summary>
    /// <param name="owner">The owner's user id.</param>
    /// <param name="nonce">The nonce the Manifest carries.</param>
    /// <returns><c>H("axiom-mesh/package/1" ‖ owner ‖ nonce)</c>.</returns>
    public static PackageId Derive(UserId owner, PackageNonce nonce)
    {
        ReadOnlySpan<byte> domain = DomainUtf8;
        Span<byte> input = stackalloc byte[domain.Length + (2 * Hash32.Length)];
        domain.CopyTo(input);
        owner.Value.CopyTo(input[domain.Length..]);
        nonce.Value.CopyTo(input[(domain.Length + Hash32.Length)..]);
        return new PackageId(Hash32.Of(input));
    }

    /// <summary>Reads 32 bytes (SPEC.md §5.2). Check <c>reader.Ok</c>.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The value read.</returns>
    public static PackageId Read(ref WireReader reader) => new(Hash32.Read(ref reader));

    /// <summary>Parses 64 hex digits.</summary>
    /// <param name="hex">The text.</param>
    /// <param name="value">The value, or zero.</param>
    /// <returns><see langword="true"/> when the text was 64 hex digits.</returns>
    public static bool TryParseHex(ReadOnlySpan<char> hex, out PackageId value)
    {
        bool parsed = Hash32.TryParseHex(hex, out Hash32 bytes);
        value = new PackageId(bytes);
        return parsed;
    }

    /// <summary>Writes the 32 bytes.</summary>
    /// <param name="writer">The writer.</param>
    public void Write(ref WireWriter writer) => Value.Write(ref writer);

    /// <inheritdoc />
    public int CompareTo(PackageId other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => Value.ToHex();

    /// <summary>Whether <paramref name="left"/> sorts before <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator <(PackageId left, PackageId right) => left.Value < right.Value;

    /// <summary>Whether <paramref name="left"/> sorts after <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator >(PackageId left, PackageId right) => left.Value > right.Value;

    /// <summary>Whether <paramref name="left"/> sorts before or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator <=(PackageId left, PackageId right) => left.Value <= right.Value;

    /// <summary>Whether <paramref name="left"/> sorts after or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator >=(PackageId left, PackageId right) => left.Value >= right.Value;
}
