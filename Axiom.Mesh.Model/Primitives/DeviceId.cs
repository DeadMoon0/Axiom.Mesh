using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// A device's id: <c>H(device public key)</c> over the 65-byte encoding (SPEC.md §2.3). Obtained from a key with <see cref="PublicKey.AsDeviceId"/>; §4.5 compares two of them as 32-byte unsigned big-endian integers, which is this type's order. Ordered as unsigned byte strings.
/// </summary>
/// <param name="Value">The 32 bytes.</param>
[ClosedShape("An id is one SHA-256 output (SPEC.md §2.3).")]
public readonly record struct DeviceId(Hash32 Value) : IComparable<DeviceId>
{
    /// <summary>Reads 32 bytes (SPEC.md §5.2). Check <c>reader.Ok</c>.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The value read.</returns>
    public static DeviceId Read(ref WireReader reader) => new(Hash32.Read(ref reader));

    /// <summary>Parses 64 hex digits.</summary>
    /// <param name="hex">The text.</param>
    /// <param name="value">The value, or zero.</param>
    /// <returns><see langword="true"/> when the text was 64 hex digits.</returns>
    public static bool TryParseHex(ReadOnlySpan<char> hex, out DeviceId value)
    {
        bool parsed = Hash32.TryParseHex(hex, out Hash32 bytes);
        value = new DeviceId(bytes);
        return parsed;
    }

    /// <summary>Writes the 32 bytes.</summary>
    /// <param name="writer">The writer.</param>
    public void Write(ref WireWriter writer) => Value.Write(ref writer);

    /// <inheritdoc />
    public int CompareTo(DeviceId other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => Value.ToHex();

    /// <summary>Whether <paramref name="left"/> sorts before <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator <(DeviceId left, DeviceId right) => left.Value < right.Value;

    /// <summary>Whether <paramref name="left"/> sorts after <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator >(DeviceId left, DeviceId right) => left.Value > right.Value;

    /// <summary>Whether <paramref name="left"/> sorts before or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator <=(DeviceId left, DeviceId right) => left.Value <= right.Value;

    /// <summary>Whether <paramref name="left"/> sorts after or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator >=(DeviceId left, DeviceId right) => left.Value >= right.Value;
}
