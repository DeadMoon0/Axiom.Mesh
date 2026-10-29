using System;

namespace Axiom.Mesh.Core;

/// <summary>
/// Where a node's randomness comes from: key creation, Package nonces, temporary file names and service tokens
/// (fundamentals plan §3.2). The one seam randomness enters by, so that a test fixes it with a seed.
/// </summary>
public interface IEntropy
{
    /// <summary>Fills <paramref name="destination"/> with random bytes.</summary>
    /// <param name="destination">The bytes to fill.</param>
    void Fill(Span<byte> destination);
}
