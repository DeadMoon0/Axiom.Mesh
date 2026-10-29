using System;
using System.Collections.Generic;
using System.Threading;

namespace Axiom.Mesh.Core;

/// <summary>
/// The device credential a running node presents, and the Entry writer that signs with the same key: empty while the
/// node has no device key, filled once at start or when an identity is created on the running node (LOCAL-SPEC.md
/// §6.1), and read lock-free by every handshake. A credential is never replaced while it may be in use; the node
/// releases them when it stops.
/// </summary>
internal sealed class CredentialSlot : IDisposable
{
    private readonly object gate = new();
    private readonly StoreWriter writer;
    private readonly List<IDisposable> owned = [];
    private DeviceCredential? credential;
    private EntryWriter? entries;

    /// <summary>A slot holding the credential a node started with, if any.</summary>
    /// <param name="writer">The store writer the Entry writer stores through.</param>
    /// <param name="started">The credential read at start; <see langword="null"/> when the key store held no device key.</param>
    public CredentialSlot(StoreWriter writer, DeviceCredential? started)
    {
        this.writer = writer;
        if (started is not null)
            Install(started);
    }

    /// <summary>The credential presented in handshakes; <see langword="null"/> while there is none.</summary>
    public DeviceCredential? Credential => Volatile.Read(ref credential);

    /// <summary>The writer of this device's Entries; <see langword="null"/> while there is no device key.</summary>
    public EntryWriter? Entries => Volatile.Read(ref entries);

    /// <summary>Fills the slot with a device key's credential, unless it holds one already (debt 25).</summary>
    /// <param name="installed">The credential; the slot owns it from now on.</param>
    /// <returns>The credential the slot holds now: <paramref name="installed"/>, or the one it held.</returns>
    public DeviceCredential Install(DeviceCredential installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        lock (gate)
        {
            if (credential is not null)
            {
                installed.Dispose();
                return credential;
            }

            var writing = new EntryWriter(writer, installed.Signer);
            owned.Add(installed);
            owned.Add(writing);
            Volatile.Write(ref entries, writing);
            Volatile.Write(ref credential, installed);
            return installed;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            foreach (IDisposable disposable in owned)
                disposable.Dispose();
            owned.Clear();
        }
    }
}
