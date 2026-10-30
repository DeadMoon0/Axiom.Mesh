using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// Writes an Entry on this device (SPEC.md §9.3; LOCAL-SPEC.md §9.4): chooses clock and parents with
/// <see cref="ParentRule"/> from the own Head and the stored Entry highest in the total order, or takes the parents the
/// writer named; signs with the device key, admits it against the node's trust like any Entry, and stores it. Writes
/// are taken one at a time, so two never choose the same clock from the same Heads. The objects its payload names should
/// be stored first: then they are pinned <c>Outbound</c> until sent once (§11.2).
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

    /// <summary>Writes one Entry with the parents of SPEC.md §9.3.</summary>
    /// <param name="package">The Package; followed, with its Manifest held.</param>
    /// <param name="payload">What the Entry says.</param>
    /// <param name="cancellationToken">Cancels waiting for the previous write or the store.</param>
    /// <returns>The Entry's hash, once it is stored.</returns>
    /// <exception cref="MeshNodeException">No identity, the Package is not followed, the device may not write to it, its
    /// clock is exhausted, or the store is full.</exception>
    public async Task<ObjectHash> WriteAsync(PackageId package, EntryPayload payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        EntryWritten written = await WriteAsync(new EntryWrite(package, payload, EntryParents.None, []), cancellationToken).ConfigureAwait(false);
        return written.Status == Status.Ok
            ? written.Hash
            : throw new MeshNodeException(MeshFault.Caller, written.Message, StepFor(written.Status));
    }

    /// <summary>Writes one Entry (LOCAL-SPEC.md §9.4): the parents named, or the Host's choice when none are.</summary>
    /// <param name="write">What the Entry says.</param>
    /// <param name="cancellationToken">Cancels waiting for the previous write or the store.</param>
    /// <returns>The Entry stored, or why not.</returns>
    public async Task<EntryWritten> WriteAsync(EntryWrite write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        await one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TrustSnapshot trust = writer.Trust;
            PackageId package = write.Package;
            if (trust.Self is not { } self || self.Device != device.PublicKey.AsDeviceId())
                return EntryWritten.Refused(Status.NoIdentity, "This node has no identity yet, so it cannot write an Entry.");
            if (!trust.IsFollowed(package) || !trust.TryGetManifest(package, out _))
                return EntryWritten.Refused(Status.NotFound, $"Package {package} is not followed here with its Manifest, so nothing can be written to it.");

            await using LdbContext reading = await LdbContext.OpenAsync(writer.Database, cancellationToken).ConfigureAwait(false);
            (EntryKey? ownHead, EntryKey? highest) = await HeadsAsync(reading, package, self.Device, cancellationToken).ConfigureAwait(false);
            List<EntryFacts>? named = null;
            if (!write.Parents.IsEmpty)
            {
                named = await NamedParentsAsync(reading, write.Parents, cancellationToken).ConfigureAwait(false);
                if (named is null || highest is null || named.Exists(p => p.Package != package))
                    return EntryWritten.Refused(Status.Invalid, $"Every parent must be a stored Entry of Package {package}.");
            }

            (ulong Clock, EntryParents Parents)? chosen = Choose(ownHead, highest, write.Parents);
            if (chosen is not { } choice)
                return EntryWritten.Refused(Status.ClockExhausted, $"The next clock of Package {package} would not be well-formed (SPEC.md §9.3): this device can write no more to it.");

            VerifiedEntry signed = new EntryDraft { Package = package, Clock = choice.Clock, Parents = choice.Parents, Payload = write.Payload }.Sign(device);
            if (named is not null && Lamport.TryFindParentFault(signed.Content, named, out ObjectFault fault))
                return EntryWritten.Refused(Status.Invalid, $"With the parents named the Entry would be faulty: {fault.Detail}");
            if (EntryAdmission.Decide(signed, trust) is not AdmissionDecision.Admit admit)
                return EntryWritten.Refused(Status.NotWritable, $"This device may not write to Package {package}: its user is neither the owner nor a Write grantee, or the device is removed (SPEC.md §9.6).");

            AdmitResult stored = await writer.SendAsync(new AdmitCommand(admit.Entry, Origin.Local, package, null) { References = write.Referenced }, cancellationToken).ConfigureAwait(false);
            return stored.Outcome switch
            {
                AdmitOutcome.Stored or AdmitOutcome.AlreadyStored => new EntryWritten(Status.Ok, admit.Entry.Hash, stored.StoreSeq, string.Empty),
                AdmitOutcome.StoreFull => EntryWritten.Refused(Status.StoreFull, "The store's pinned bytes would exceed its budget (LOCAL-SPEC.md §6.5); release pins or raise the budget."),
                _ => EntryWritten.Refused(Status.Invalid, $"The Entry was not stored: {stored.Outcome}."),
            };
        }
        finally
        {
            one.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => one.Dispose();

    // SPEC.md §9.3: the clock is one above the highest stored; named parents are kept, else the parent rule chooses.
    private static (ulong Clock, EntryParents Parents)? Choose(EntryKey? ownHead, EntryKey? highest, EntryParents named)
    {
        if (named.IsEmpty)
            return ParentRule.Choose(ownHead, highest) is ParentChoice.Write write ? (write.Clock, write.Parents) : null;
        ulong clock = highest!.Value.Clock + 1;
        return clock < Limits.ClockLimit ? (clock, named) : null;
    }

    private static string StepFor(Status status) => status switch
    {
        Status.NoIdentity => "Create or adopt an identity first.",
        Status.NotFound => "Follow the Package and wait until its Manifest is stored.",
        Status.NotWritable => "Ask the owner for a Write grant.",
        Status.StoreFull => "Free space in the store or raise its budget, then write again.",
        _ => "Write to another Package.",
    };

    // The facts of each parent named, or null when one is not a stored Entry.
    private static async Task<List<EntryFacts>?> NamedParentsAsync(LdbContext reading, EntryParents parents, CancellationToken cancellationToken)
    {
        var facts = new List<EntryFacts>(parents.Count);
        foreach (ObjectHash parent in parents)
        {
            if (await reading.Set<EntryRow>().FindAsync(RowKeys.Of(parent.Value), cancellationToken).ConfigureAwait(false) is not { } row)
                return null;
            facts.Add(new EntryFacts(new PackageId(RowKeys.Hash(row.Package)), row.Kind, EntryIndex.KeyOf(row)));
        }

        return facts;
    }

    // The own Head, and the stored Entry highest in the total order: among the Heads the highest clock, then every stored
    // Entry of that clock, since a device's Head is its lowest hash of a clock and the order wants the highest.
    private async Task<(EntryKey? OwnHead, EntryKey? Highest)> HeadsAsync(LdbContext reading, PackageId package, DeviceId self, CancellationToken cancellationToken)
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

        List<EntryRow> tied = await reading.Set<EntryRow>().ReadOnlyQuery()
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
}
