using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Local;
using LocalStoredObject = Axiom.Mesh.Local.StoredObject;

namespace Axiom.Mesh.Core;

/// <summary>
/// The service role's requests of LOCAL-SPEC.md §9: <c>RegisterKind</c> (§9.2), <c>WriteEntry</c> (§9.4),
/// <c>PutObject</c> (§9.5) and <c>GetStorePath</c> (§9.7). <c>AddTelemetry</c> and <c>Emit</c> are deferred (plan D7);
/// <c>PutFile</c> was removed in draft 4 (the client's helpers cut files).
/// </summary>
internal static class ServiceRequests
{
    /// <summary>The store layout <c>GetStorePath</c> names: <c>objects/&lt;h0h1&gt;/&lt;h2h3&gt;/&lt;hash&gt;</c> (§9.7).</summary>
    public const byte StoreLayout = 1;

    /// <summary><c>RegisterKind</c> (512): the kind held by this connection while it lasts, as <see cref="KindRule"/> decides.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> RegisterKindAsync(RequestContext context, uint correlation, RegisterKind request, CancellationToken cancellationToken)
    {
        LocalService service = context.Connection.Service!;
        if (context.Port.Kinds.TryRegister(context.Connection, service, request.Kind, request.EntryKinds) is { } refusal)
            return ValueTask.FromResult(refusal.Answer(correlation));

        StoreWriter writer = context.Node.Writer;
        uint followed = (uint)writer.Trust.Packages.Count(p => p.IsFollowed && p.Manifest?.Content.Kind == request.Kind);
        var registration = new KindRegistration { StoreEpoch = writer.Epoch, StoreSeq = writer.Totals.StoreSeq, Packages = followed };
        return ValueTask.FromResult(LocalAnswer.Ok<RegisterKind, KindRegistration>(correlation, registration));
    }

    /// <summary>
    /// <c>WriteEntry</c> (513): an Entry of a registered kind in a followed Package, its payload opaque and never parsed, its
    /// references and <c>redacts</c> copied into the envelope, every reference stored already, its parents named or
    /// chosen, signed with the device key and stored (§9.4).
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> WriteEntryAsync(RequestContext context, uint correlation, WriteEntry request, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        TrustSnapshot trust = node.Trust;
        if (!trust.IsFollowed(request.PackageId) || !trust.TryGetManifest(request.PackageId, out VerifiedManifest? manifest))
            return PackageRequests.Unknown(correlation, request.PackageId);
        if (!context.Port.Kinds.Holds(context.Connection, manifest.Content.Kind, request.Kind))
            return LocalAnswer.Failure(correlation, Status.KindNotRegistered, $"This service did not register Package kind {(ushort)manifest.Content.Kind} with Entry kind {(ushort)request.Kind}.", "kind");
        if (WriteRule.Check(request, out EntryBody? body) is { } refusal)
            return refusal.Answer(correlation);

        var write = new EntryWrite(request.PackageId, body!, WriteRule.ParentsOf(request.Parents));
        foreach (ObjectHash referenced in body!.References)
        {
            if (!await node.ContainsAsync(referenced, cancellationToken).ConfigureAwait(false))
                return LocalAnswer.Failure(correlation, Status.NotFound, $"The referenced object {referenced} is not stored; put it first.", "references");
        }

        EntryWriter writer = node.Credentials.Entries!;
        EntryWritten written = await writer.WriteAsync(write, cancellationToken).ConfigureAwait(false);
        if (written.Status != Status.Ok)
            return LocalAnswer.Failure(correlation, written.Status, written.Message, written.Status == Status.Invalid ? "parents" : string.Empty);
        if (await EntryOfAsync(node, written.Hash, cancellationToken).ConfigureAwait(false) is not { } entry)
            return LocalAnswer.Failure(correlation, Status.IoError, "The Entry was stored and evicted before it could be read back.", string.Empty);
        return LocalAnswer.Ok<WriteEntry, Entry>(correlation, entry);
    }

    /// <summary>
    /// <c>PutObject</c> (514): an unsigned object checked as SPEC.md §8.1 to §8.4 say, stored atomically and pinned with reason
    /// <c>Service</c> for this service; a signed or malformed object is <c>Invalid</c>.
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> PutObjectAsync(RequestContext context, uint correlation, PutObject request, CancellationToken cancellationToken)
    {
        if (PutRule.Check(request.Object.ToArray(), out StorableObject? storable) is { } refusal)
            return refusal.Answer(correlation);

        // The object and its pin are queued back to back, so they usually commit in one batch: a put costs one commit, and
        // concurrent puts (PutObject writes no Package and is not ordered, LOCAL-SPEC.md §3.4) share theirs. A pin of an object
        // the store refused is a no-op.
        MeshNode node = context.Node;
        (PinReason reason, Hash32 source) = StoreRequests.PinOwner(context.Connection);
        Task<AdmitResult> admitted = await node.Writer.EnqueueAsync(new AdmitCommand(storable!, Origin.Local, null, null), cancellationToken).ConfigureAwait(false);
        Task<int> pinned = await node.Writer.EnqueueAsync(new PinCommand(PinCommandKind.Pin, [storable!.Hash], reason, source), cancellationToken).ConfigureAwait(false);
        AdmitResult stored = await admitted.ConfigureAwait(false);
        await pinned.ConfigureAwait(false);
        if (stored.Outcome == AdmitOutcome.StoreFull)
            return PackageRequests.StoreFull(correlation);
        return LocalAnswer.Ok<PutObject, LocalStoredObject>(correlation, new LocalStoredObject { Hash = storable.Hash, Stored = stored.Outcome == AdmitOutcome.Stored });
    }

    /// <summary>
    /// <c>GetStorePath</c> (516): the store directory and layout 1, for direct reads (§9.7); <c>NotPermitted</c> when the
    /// directory is not readable by this connection's user (Host capability bit 0 clear).
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> GetStorePathAsync(RequestContext context, uint correlation, GetStorePath request, CancellationToken cancellationToken)
    {
        if (!context.Port.AckFor(context.Connection).Capabilities.HasFlag(HostCapabilities.StoreReadable) || context.Node.Files.StorePath is not { } path)
            return ValueTask.FromResult(LocalAnswer.Failure(correlation, Status.NotPermitted, "The store directory is not readable by this connection's user; read objects with GetObject.", string.Empty));
        return ValueTask.FromResult(LocalAnswer.Ok<GetStorePath, StorePath>(correlation, new StorePath { Path = path, Layout = StoreLayout }));
    }

    /// <summary>The record of a stored Entry by its hash.</summary>
    /// <param name="node">The node.</param>
    /// <param name="hash">The Entry's hash.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The record; <see langword="null"/> when it is not stored.</returns>
    public static async Task<Entry?> EntryOfAsync(MeshNode node, ObjectHash hash, CancellationToken cancellationToken)
    {
        EntryRow? row;
        await using (LdbContext reading = await LdbContext.OpenAsync(node.Writer.Database, cancellationToken).ConfigureAwait(false))
            row = await reading.Set<EntryRow>().FindAsync(RowKeys.Of(hash.Value), cancellationToken).ConfigureAwait(false);
        return row is null ? null : await HistoryRequests.RecordOfAsync(node.Files, row, cancellationToken).ConfigureAwait(false);
    }
}
