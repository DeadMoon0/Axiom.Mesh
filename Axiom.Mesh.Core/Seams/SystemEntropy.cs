using System;
using System.Security.Cryptography;

namespace Axiom.Mesh.Core;

/// <summary>The operating system's cryptographically secure random number generator: what every real node uses.</summary>
public sealed class SystemEntropy : IEntropy
{
    /// <inheritdoc />
    public void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
}
