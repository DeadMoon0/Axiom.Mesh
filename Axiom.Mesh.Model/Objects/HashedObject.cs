using System;

namespace Axiom.Mesh;

/// <summary>
/// Object bytes together with their name, <c>H(bytes)</c>, proven to match (SPEC.md §7.6, §8.1): the first state of the
/// object typestate, and the only way into <see cref="Objects.TryParse"/>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is copied: the value refers to the caller's memory, and so does everything parsed from it. The bytes must
/// not change while this value or anything derived from it is in use; hand in memory the caller owns, or a frame view
/// only for as long as the frame stays valid.
/// </para>
/// <para><c>default</c> holds no bytes and parses as malformed.</para>
/// </remarks>
public readonly struct HashedObject
{
    private HashedObject(ReadOnlyMemory<byte> bytes, ObjectHash hash)
    {
        Bytes = bytes;
        Hash = hash;
    }

    /// <summary>Every byte of the object.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>The object's name, <c>H(Bytes)</c>.</summary>
    public ObjectHash Hash { get; }

    /// <summary>Names bytes this side holds: its own objects, its own store.</summary>
    /// <param name="bytes">Every byte of the object.</param>
    /// <returns>The bytes with their hash.</returns>
    public static HashedObject Of(ReadOnlyMemory<byte> bytes) => new(bytes, ObjectHash.Of(bytes.Span));

    /// <summary>
    /// Checks bytes a peer sent against the hash it claimed for them (SPEC.md §7.6); a mismatch is a violation.
    /// </summary>
    /// <param name="bytes">Every byte of the object.</param>
    /// <param name="claimed">The hash the sender named.</param>
    /// <param name="hashed">The bytes with their hash, or <c>default</c>.</param>
    /// <returns><see langword="true"/> when <c>H(bytes)</c> equals <paramref name="claimed"/>.</returns>
    public static bool TryVerify(ReadOnlyMemory<byte> bytes, ObjectHash claimed, out HashedObject hashed)
    {
        ObjectHash actual = ObjectHash.Of(bytes.Span);
        hashed = actual == claimed ? new HashedObject(bytes, actual) : default;
        return actual == claimed;
    }
}
