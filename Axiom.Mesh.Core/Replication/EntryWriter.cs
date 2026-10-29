using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;

namespace Axiom.Mesh.Core;

/// <summary>
/// Writes an Entry on this device (SPEC.md §9.3; LOCAL-SPEC.md §9.4): chooses clock and parents with
/// <see cref="ParentRule"/> from the own Head and the stored Entry highest in the total order, signs with the device
/// key, admits it against the node's trust like any Entry, and stores it. Writes are taken one at a time, so two never
/// choose the same clock from the same Heads. The objects its payload names should be stored first: then they are
/// pinned <c>Outbound</c> until sent once (§11.2).
/// </summary>
internal sealed class EntryWriter : IDisposable
{
    private readonly StoreWriter writer;
    private readonly ISigner device;
    private readonly SemaphoreSlim one = new(1, 1);

    /// <summary>A writer for this device.</summary>
    /// <param name="writer">The store writer.</param>
    /// <param name="device">The device key; the private half stays inside it.</param>
    public EntryWriter(StoreWriter writer, ISigner device)
    {
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.device = device ?? throw new ArgumentNullException(nameof(device));
    }

    /// <summary>Writes one Entry.</summary>
    /// <param name="package">The Package; followed, with its Manifest held.</param>
    /// <param name="payload">What the Entry says.</param>
    /// <param name="cancellationToken">Cancels waiting for the previous write or the store.</param>
    /// <returns>The Entry's hash, once it is stored.</returns>
    /// <exception cref="MeshNodeException">No identity, the Package is not followed, the device may not write to it, its
    /// clock is exhausted, or the store is full.</exception>
    public async Task<ObjectHash> WriteAsync(PackageId package, EntryPayload payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        await one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TrustSnapshot trust = writer.Trust;
            if (trust.Self is not { } self || self.Device != device.PublicKey.AsDeviceId())
                throw Refused("This node has no identity yet, so it cannot write an Entry.", "Create or adopt an identity first.");
            if (!trust.TryGetManifest(package, out _))
                throw Refused($"Package {package} is not followed here with its Manifest, so nothing can be written to it.", "Follow the Package and wait until its Manifest is stored.");

            (EntryKey? ownHead, EntryKey? highest) = await HeadsAsync(package, self.Device, cancellationToken).ConfigureAwait(false);
            if (ParentRule.Choose(ownHead, highest) is not ParentChoice.Write choice)
                throw Refused($"The next clock of Package {package} would not be well-formed (SPEC.md §9.3): this device can write no more to it.", "Write to another Package.");

            VerifiedEntry signed = new EntryDraft { Package = package, Clock = choice.Clock, Parents = choice.Parents, Payload = payload }.Sign(device);
            if (EntryAdmission.Decide(signed, trust) is not AdmissionDecision.Admit admit)
                throw Refused($"This device may not write to Package {package}: its user is neither the owner nor a Write grantee (SPEC.md §9.6).", "Ask the owner for a Write grant.");

            AdmitResult stored = await writer.SendAsync(new AdmitCommand(admit.Entry, Origin.Local, package, null), cancellationToken).ConfigureAwait(false);
            if (!stored.IsStored)
                throw Refused($"The Entry was not stored: {stored.Outcome} (LOCAL-SPEC.md §6.5).", "Free space in the store or raise its budget, then write again.");
            return admit.Entry.Hash;
        }
        finally
        {
            one.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => one.Dispose();

    // The own Head, and the stored Entry highest in the total order: among the Heads the highest clock, then every stored
    // Entry of that clock, since a device's Head is its lowest hash of a clock and the order wants the highest.
    private async Task<(EntryKey? OwnHead, EntryKey? Highest)> HeadsAsync(PackageId package, DeviceId self, CancellationToken cancellationToken)
    {
        IReadOnlyList<Head> heads = writer.Heads.Of(package);
        if (heads.Count == 0)
            return (null, null);

        EntryKey? own = null;
        ulong top = 0;
        foreach (Head head in heads)
        {
            if (head.Device == self)
                own = new EntryKey(head.Clock, head.Device, head.Entry);
            top = Math.Max(top, head.Clock);
        }

        await using LdbContext context = await LdbContext.OpenAsync(writer.Database, cancellationToken).ConfigureAwait(false);
        List<EntryRow> tied = await context.Set<EntryRow>().ReadOnlyQuery()
            .Where(EntryRow.Columns.Package == RowKeys.Of(package.Value) & EntryRow.Columns.Clock == top)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        EntryKey? highest = null;
        foreach (EntryRow row in tied)
        {
            EntryKey key = EntryIndex.KeyOf(row);
            if (highest is null || key > highest.Value)
                highest = key;
        }

        return (own, highest);
    }

    private static MeshNodeException Refused(string message, string step) => new(MeshFault.Caller, message, step);
}
