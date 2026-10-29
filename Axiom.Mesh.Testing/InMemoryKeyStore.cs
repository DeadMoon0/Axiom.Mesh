using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Testing;

/// <summary>
/// A key store in memory: what a node of a test mesh keeps its keys in. It survives the node's restart because the
/// mesh keeps it, not the node. Thread-safe.
/// </summary>
public sealed class InMemoryKeyStore : IKeyStore
{
    private readonly object gate = new();
    private readonly Dictionary<KeyRole, byte[]> keys = [];

    /// <summary>A key store holding the device key of a published test device.</summary>
    /// <param name="identity">The device.</param>
    /// <returns>The key store.</returns>
    public static InMemoryKeyStore For(TestIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var store = new InMemoryKeyStore();
        store.keys[KeyRole.Device] = identity.DevicePkcs8();
        return store;
    }

    /// <inheritdoc />
    public ValueTask<byte[]?> ReadAsync(KeyRole role, CancellationToken cancellationToken)
    {
        lock (gate)
            return ValueTask.FromResult(keys.TryGetValue(role, out byte[]? key) ? (byte[]?)key.Clone() : null);
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(KeyRole role, ReadOnlyMemory<byte> pkcs8, CancellationToken cancellationToken)
    {
        if (role == KeyRole.None)
            throw new ArgumentOutOfRangeException(nameof(role), role, "Store a device or a user key.");
        lock (gate)
            keys[role] = pkcs8.ToArray();
        return ValueTask.CompletedTask;
    }
}
