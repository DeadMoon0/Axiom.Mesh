using System;
using System.Buffers;

namespace Axiom.Mesh;

/// <summary>The digest a signature covers (SPEC.md §2.2).</summary>
public static class Digests
{
    private const int StackLimit = 1024;

    /// <summary>
    /// <c>H(domain ‖ body)</c>, where <paramref name="body"/> is every byte of the structure that precedes its signature,
    /// exactly as received: a verifier hashes the bytes it got, never a re-encoding of its own (SPEC.md §2.2).
    /// </summary>
    /// <param name="domain">The structure's domain.</param>
    /// <param name="body">The bytes before the signature.</param>
    /// <returns>The digest.</returns>
    public static SigningDigest Of(SigningDomain domain, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(domain);
        int length = domain.Utf8.Length + body.Length;
        byte[]? rented = length > StackLimit ? ArrayPool<byte>.Shared.Rent(length) : null;
        Span<byte> input = rented is null ? stackalloc byte[length] : rented.AsSpan(0, length);
        try
        {
            domain.Utf8.CopyTo(input);
            body.CopyTo(input[domain.Utf8.Length..]);
            return new SigningDigest(Hash32.Of(input));
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
