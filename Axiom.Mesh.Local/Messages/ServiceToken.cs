using System;
using System.Security.Cryptography;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// The 32 bytes a service presents in <c>Hello.token</c> (LOCAL-SPEC.md §4.2): the token the Host put into a spawned
/// child's environment, or the one its configuration holds for an external service. All zero for a user client and
/// for an in-process service.
/// </summary>
/// <remarks>
/// A token opens the service role of one name and nothing else; it is not a key (§13). It never prints itself, and two
/// tokens compare in time independent of their content.
/// </remarks>
public readonly struct ServiceToken : IEquatable<ServiceToken>
{
    /// <summary>The environment variable a spawned service finds its token in, as 64 lowercase hex characters (§4.2).</summary>
    public const string EnvironmentVariable = "AXIOM_MESH_SERVICE_TOKEN";

    private readonly Hash32 bytes;

    private ServiceToken(Hash32 bytes) => this.bytes = bytes;

    /// <summary>The token of a user client or an in-process service: all zero.</summary>
    public static ServiceToken None => default;

    /// <summary>Whether this is <see cref="None"/>.</summary>
    public bool IsNone => bytes.IsZero;

    /// <summary>A token of exactly 32 bytes.</summary>
    /// <param name="value">The bytes.</param>
    /// <returns>The token.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not 32 bytes long.</exception>
    public static ServiceToken FromBytes(ReadOnlySpan<byte> value)
    {
        if (value.Length != Limits.ServiceTokenLength)
            throw new ArgumentException($"A service token is {Limits.ServiceTokenLength} bytes, not {value.Length} (LOCAL-SPEC.md §4.2).", nameof(value));
        return new ServiceToken(new Hash32(value));
    }

    /// <summary>Parses a token written as 64 hex characters, as the environment and the configuration hold it.</summary>
    /// <param name="hex">The text.</param>
    /// <param name="token">The token.</param>
    /// <returns><see langword="true"/> when the text is 64 hex characters.</returns>
    public static bool TryParseHex(ReadOnlySpan<char> hex, out ServiceToken token)
    {
        bool parsed = Hash32.TryParseHex(hex, out Hash32 value);
        token = parsed ? new ServiceToken(value) : default;
        return parsed;
    }

    /// <summary>Copies the 32 bytes, for the Host that keeps or compares them.</summary>
    /// <param name="destination">At least 32 bytes.</param>
    public void CopyTo(Span<byte> destination) => bytes.CopyTo(destination);

    /// <summary>Compares two tokens in time independent of their content (§4.2, check 3).</summary>
    /// <param name="other">The other token.</param>
    /// <returns><see langword="true"/> when the 32 bytes are equal.</returns>
    public bool FixedTimeEquals(ServiceToken other)
    {
        Span<byte> mine = stackalloc byte[Limits.ServiceTokenLength];
        Span<byte> theirs = stackalloc byte[Limits.ServiceTokenLength];
        bytes.CopyTo(mine);
        other.bytes.CopyTo(theirs);
        return CryptographicOperations.FixedTimeEquals(mine, theirs);
    }

    /// <inheritdoc />
    public bool Equals(ServiceToken other) => FixedTimeEquals(other);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ServiceToken other && FixedTimeEquals(other);

    /// <summary>A constant: a hash code would leak bits of the token into every dictionary it is put in.</summary>
    /// <returns>Zero.</returns>
    public override int GetHashCode() => 0;

    /// <summary>Never the token itself, so that it cannot reach a log.</summary>
    /// <returns><c>ServiceToken(none)</c> or <c>ServiceToken(set)</c>.</returns>
    public override string ToString() => IsNone ? "ServiceToken(none)" : "ServiceToken(set)";

    /// <summary>Whether two tokens are equal, compared in fixed time.</summary>
    /// <param name="left">A token.</param>
    /// <param name="right">Another token.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(ServiceToken left, ServiceToken right) => left.FixedTimeEquals(right);

    /// <summary>Whether two tokens differ, compared in fixed time.</summary>
    /// <param name="left">A token.</param>
    /// <param name="right">Another token.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(ServiceToken left, ServiceToken right) => !left.FixedTimeEquals(right);

    internal static ServiceToken Read(ref WireReader reader) => new(Hash32.Read(ref reader));

    internal void Write(ref WireWriter writer) => bytes.Write(ref writer);
}
