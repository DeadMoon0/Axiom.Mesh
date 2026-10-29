using System;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh;

/// <summary>
/// The name of an object: <c>H(object)</c> over all of its bytes (SPEC.md §8.1). Ordered as unsigned byte strings.
/// </summary>
/// <param name="Value">The 32 bytes.</param>
/// <remarks>
/// A name says nothing about trust: anyone can hash bytes. What has been checked is said by the type that carries
/// the bytes (<see cref="HashedObject"/>, <see cref="ParsedObject"/>, <see cref="StorableObject"/>).
/// </remarks>
[ClosedShape("An object name is one SHA-256 output (SPEC.md §2.1, §8.1).")]
public readonly record struct ObjectHash(Hash32 Value) : IComparable<ObjectHash>
{
    /// <summary>Whether every byte is zero, which SPEC.md uses for "none" (§8.5 <c>replyTo</c>, §8.7 branch heads).</summary>
    public bool IsZero => Value.IsZero;

    /// <summary>The name of the given object bytes.</summary>
    /// <param name="objectBytes">Every byte of the object.</param>
    /// <returns><c>H(objectBytes)</c>.</returns>
    public static ObjectHash Of(ReadOnlySpan<byte> objectBytes) => new(Hash32.Of(objectBytes));

    /// <summary>Reads a <c>hash</c> (SPEC.md §5.2). Check <c>reader.Ok</c>.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The hash read.</returns>
    public static ObjectHash Read(ref WireReader reader) => new(Hash32.Read(ref reader));

    /// <summary>Parses 64 hex digits.</summary>
    /// <param name="hex">The text.</param>
    /// <param name="hash">The hash, or zero.</param>
    /// <returns><see langword="true"/> when the text was 64 hex digits.</returns>
    public static bool TryParseHex(ReadOnlySpan<char> hex, out ObjectHash hash)
    {
        bool parsed = Hash32.TryParseHex(hex, out Hash32 value);
        hash = new ObjectHash(value);
        return parsed;
    }

    /// <summary>Writes the 32 bytes.</summary>
    /// <param name="writer">The writer.</param>
    public void Write(ref WireWriter writer) => Value.Write(ref writer);

    /// <inheritdoc />
    public int CompareTo(ObjectHash other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => Value.ToHex();

    /// <summary>Whether <paramref name="left"/> sorts before <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator <(ObjectHash left, ObjectHash right) => left.Value < right.Value;

    /// <summary>Whether <paramref name="left"/> sorts after <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator >(ObjectHash left, ObjectHash right) => left.Value > right.Value;

    /// <summary>Whether <paramref name="left"/> sorts before or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator <=(ObjectHash left, ObjectHash right) => left.Value <= right.Value;

    /// <summary>Whether <paramref name="left"/> sorts after or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison.</returns>
    public static bool operator >=(ObjectHash left, ObjectHash right) => left.Value >= right.Value;
}
