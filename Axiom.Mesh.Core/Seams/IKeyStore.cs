using System;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core;

/// <summary>
/// Where a node's private keys live: the one seam a private key enters the engine by (plan §4). The Host keeps them in
/// files wrapped for the operating-system user; tests keep them in memory. Keys travel as PKCS#8 bytes, P-256 only.
/// </summary>
/// <remarks>
/// The engine reads the device key when it starts and builds its TLS certificate from it; it never logs, copies to a
/// message, or keeps the bytes longer than it needs them. A key the store does not hold is not an error: a device
/// without an identity has none yet (LOCAL-SPEC.md §6.1).
/// </remarks>
public interface IKeyStore
{
    /// <summary>Reads a private key.</summary>
    /// <param name="role">Which key.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The key as PKCS#8 bytes, which the caller owns and clears; <see langword="null"/> when none is held.</returns>
    ValueTask<byte[]?> ReadAsync(KeyRole role, CancellationToken cancellationToken);

    /// <summary>Stores a private key, replacing the one held for that role.</summary>
    /// <param name="role">Which key.</param>
    /// <param name="pkcs8">The key as PKCS#8 bytes; the store copies them.</param>
    /// <param name="cancellationToken">Cancels writing; a cancelled write leaves the previous key.</param>
    /// <returns>A task that completes when the key is durable.</returns>
    ValueTask WriteAsync(KeyRole role, ReadOnlyMemory<byte> pkcs8, CancellationToken cancellationToken);
}
