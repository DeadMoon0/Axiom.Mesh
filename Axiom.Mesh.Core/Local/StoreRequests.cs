using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Local;
using CorePinReason = Axiom.Mesh.Core.PinReason;
using LocalPinReason = Axiom.Mesh.Local.PinReason;

namespace Axiom.Mesh.Core;

/// <summary>
/// The pin and store requests of LOCAL-SPEC.md §6.5: <c>ListPins</c>, <c>Pin</c> (the request class <c>PinObject</c>),
/// <c>Unpin</c>, <c>ReleasePins</c>, <c>SetStoreBudget</c> (<c>GetStore</c> is in <see cref="NodeRequests"/>). The user
/// role pins with reason <c>User</c> and source zero, the service role with reason <c>Service</c> and source
/// <c>H(service name)</c> (§5.11); a client never touches the Host's own reasons.
/// </summary>
internal static class StoreRequests
{
    private const int MaxPins = 256;

    /// <summary><c>ListPins</c> (352): pins ascending by <c>(hash, reason, source)</c>, of one object or one Package's source, a page at a time.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> ListPinsAsync(RequestContext context, uint correlation, ListPins request, CancellationToken cancellationToken)
    {
        byte[]? after = request.After is { } cursor ? RowKeys.Pin(cursor.Hash, (CorePinReason)(byte)cursor.Reason, cursor.Source) : null;
        var pins = new List<Pin>(MaxPins + 1);
        await using (LdbContext reading = await LdbContext.OpenAsync(context.Node.Writer.Database, cancellationToken).ConfigureAwait(false))
        {
            List<PinRow> rows = request.Hash is { } hash
                ? await reading.Set<PinRow>().ReadOnlyQuery().Where(PinRow.Columns.Hash == RowKeys.Of(hash.Value)).ToListAsync(cancellationToken).ConfigureAwait(false)
                : request.PackageId is { } package
                    ? await reading.Set<PinRow>().ReadOnlyQuery().Where(PinRow.Columns.Source == RowKeys.Of(package.Value)).ToListAsync(cancellationToken).ConfigureAwait(false)
                    : await reading.Set<PinRow>().ReadOnlyQuery().ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (PinRow row in PinOrder.After(rows, after).Take(MaxPins + 1))
            {
                ObjectRow? stored = await reading.Set<ObjectRow>().FindAsync(row.Hash, cancellationToken).ConfigureAwait(false);
                pins.Add(PinOf(row, stored?.Size ?? 0));
            }
        }

        (List<Pin> page, bool more) = PageCut.Take(pins, MaxPins, PageCut.FrameBudget, FrameSizes.OfRecord);
        return LocalAnswer.Ok<ListPins, PinPage>(correlation, new PinPage { Pins = page, More = more });
    }

    /// <summary><c>Pin</c> (353, class <c>PinObject</c>): the role's pin on a stored object; idempotent; <c>NotFound</c> for an object not stored.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> PinObjectAsync(RequestContext context, uint correlation, PinObject request, CancellationToken cancellationToken)
    {
        (CorePinReason reason, Hash32 source) = PinOwner(context.Connection);
        MeshNode node = context.Node;
        await node.Writer.SendAsync(new PinCommand(PinCommandKind.Pin, [request.Hash], reason, source), cancellationToken).ConfigureAwait(false);
        await using LdbContext reading = await LdbContext.OpenAsync(node.Writer.Database, cancellationToken).ConfigureAwait(false);
        PinRow? row = await reading.Set<PinRow>().FindAsync(RowKeys.Pin(request.Hash, reason, source), cancellationToken).ConfigureAwait(false);
        ObjectRow? stored = await reading.Set<ObjectRow>().FindAsync(RowKeys.Of(request.Hash.Value), cancellationToken).ConfigureAwait(false);
        if (row is null || stored is null)
            return LocalAnswer.Failure(correlation, Status.NotFound, $"{request.Hash} is not stored here, so it cannot be pinned; Fetch it first.", "hash");
        return LocalAnswer.Ok<PinObject, Pin>(correlation, PinOf(row, stored.Size));
    }

    /// <summary><c>Unpin</c> (354): the role's pin on each object, removed where it was; counts what it removed.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> UnpinAsync(RequestContext context, uint correlation, Unpin request, CancellationToken cancellationToken)
    {
        if (!Ascending(request.Hashes))
            return LocalAnswer.Failure(correlation, Status.Invalid, "The hashes must be ascending as byte strings and unique.", "hashes");
        (CorePinReason reason, Hash32 source) = PinOwner(context.Connection);
        int removed = await context.Node.Writer.SendAsync(new PinCommand(PinCommandKind.Unpin, request.Hashes, reason, source), cancellationToken).ConfigureAwait(false);
        return LocalAnswer.Ok<Unpin, ushort>(correlation, (ushort)Math.Min(removed, ushort.MaxValue));
    }

    /// <summary><c>ReleasePins</c> (357): every pin of reason <c>User</c> or <c>Service</c> and a source; <c>Invalid</c> for the Host's own reasons.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> ReleasePinsAsync(RequestContext context, uint correlation, ReleasePins request, CancellationToken cancellationToken)
    {
        if (request.Reason is not (LocalPinReason.User or LocalPinReason.Service))
            return LocalAnswer.Failure(correlation, Status.Invalid, "Only pins of reason User or Service can be released; the Host holds the others itself.", "reason");
        int removed = await context.Node.Writer.SendAsync(new PinCommand(PinCommandKind.Release, [], (CorePinReason)(byte)request.Reason, request.Source), cancellationToken).ConfigureAwait(false);
        return LocalAnswer.Ok<ReleasePins, uint>(correlation, (uint)removed);
    }

    /// <summary><c>SetStoreBudget</c> (356): the budget from now on; 0 is the device default. Eviction may start at once (SPEC.md §11.3).</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> SetStoreBudgetAsync(RequestContext context, uint correlation, SetStoreBudget request, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        long budget = request.Bytes == 0 ? node.Writer.Options.BudgetBytes : (long)Math.Min(request.Bytes, long.MaxValue);
        await node.Writer.SendAsync(new BudgetCommand(budget), cancellationToken).ConfigureAwait(false);
        return LocalAnswer.Ok<SetStoreBudget, Store>(correlation, LocalRecords.StoreOf(node.Writer.Totals, node.Files.StorePath, node.Replicating.Intake.Held.Bytes));
    }

    /// <summary>The reason and source of a connection's own pins (§5.11, §6.5, §9.6).</summary>
    /// <param name="connection">The connection.</param>
    /// <returns><c>User</c> and zero for the user role; <c>Service</c> and <c>H(name)</c> for the service role.</returns>
    public static (CorePinReason Reason, Hash32 Source) PinOwner(LocalConnection connection) =>
        connection.Role == Role.Service
            ? (CorePinReason.Service, ServiceSource(connection.Hello!.Name))
            : (CorePinReason.User, default);

    /// <summary>The source of a service's pins: <c>H(service name)</c> over the name's UTF-8 bytes (§5.11).</summary>
    /// <param name="name">The service's name.</param>
    /// <returns>The source.</returns>
    public static Hash32 ServiceSource(string name) => Hash32.Of(Encoding.UTF8.GetBytes(name));

    private static Pin PinOf(PinRow row, long size) => new()
    {
        Hash = new ObjectHash(RowKeys.Hash(row.Hash)),
        Reason = (LocalPinReason)(byte)row.Reason,
        Source = RowKeys.Hash(row.Source),
        Since = LocalRecords.Milliseconds(row.SinceMs),
        Size = (ulong)Math.Max(0, size),
    };

    private static bool Ascending(IReadOnlyList<ObjectHash> hashes)
    {
        for (int i = 1; i < hashes.Count; i++)
        {
            if (hashes[i - 1].Value >= hashes[i].Value)
                return false;
        }

        return true;
    }
}
