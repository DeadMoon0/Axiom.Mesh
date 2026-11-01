using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;


namespace Axiom.Mesh.Chat;

/// <summary>
/// The chat service on one connection (CHAT-SERVICE.md §10, §11): the indexer that takes in Entries and Package changes,
/// keeps Channels in step with what the Host follows, and emits the namespace's events. Made by
/// <see cref="ChatService"/> for each <see cref="ServiceSession"/>; ended when the connection ends.
/// </summary>
internal sealed class ChatSession
{
    private static IReadOnlyList<EntryKind> Kinds => [EntryKind.Message, EntryKind.Redaction];

    private readonly CancellationTokenSource ending = new();

    /// <summary>The service on a connection.</summary>
    /// <param name="service">The runtime's session.</param>
    /// <param name="index">The index.</param>
    /// <param name="jobs">The jobs, shared across connections.</param>
    /// <param name="time">The service's clock.</param>
    public ChatSession(ServiceSession service, ChatIndex index, JobBook jobs, TimeProvider time)
    {
        Service = service;
        Index = index;
        Jobs = jobs;
        Time = time;
        Directory = new UserDirectory(service.Client);
        LiveFrom = service.Host.StoreSeq;
    }

    /// <summary>The runtime's session.</summary>
    public ServiceSession Service { get; }

    /// <summary>The connection.</summary>
    public LocalClient Client => Service.Client;

    /// <summary>The index.</summary>
    public ChatIndex Index { get; }

    /// <summary>The jobs.</summary>
    public JobBook Jobs { get; }

    /// <summary>The service's clock.</summary>
    public TimeProvider Time { get; }

    /// <summary>The users and Rosters known.</summary>
    public UserDirectory Directory { get; }

    /// <summary>The newest store sequence when the connection opened: an Entry above it arrived live and gets a <c>message</c> event.</summary>
    public ulong LiveFrom { get; }

    /// <summary>Cancelled when the connection ends: jobs that work on it stop.</summary>
    public CancellationToken Ending => ending.Token;

    /// <summary>
    /// Brings the index in step on connecting (CHAT-SERVICE.md §10): keeps, discards or discards and rebuilds it by
    /// <see cref="CatchUpPlan"/>, then makes its Channels what the Host lists of the Channel kind.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the service stops.</param>
    /// <returns>A task that completes when the index is ready for the runtime's catch-up.</returns>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        IndexMeta meta = await Index.ReadMetaAsync(cancellationToken).ConfigureAwait(false);
        IndexAction action = CatchUpPlan.Decide(meta.SchemaVersion, meta.StoreEpoch, Service.CatchUp, Service.StoreEpoch);
        ChatLog.Connected(Service.Log, action, meta.SchemaVersion, meta.LastSeq, Service.CatchUp.FromSeq);
        if (action == IndexAction.Keep)
            await Index.SetEpochAsync(Service.StoreEpoch, cancellationToken).ConfigureAwait(false);
        else
            await Index.DiscardAsync(Service.StoreEpoch, action == IndexAction.Discard ? 0 : Service.CatchUp.FromSeq - 1, cancellationToken).ConfigureAwait(false);
        await ReconcileAsync(rebuild: action == IndexAction.DiscardAndRebuild, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes in one Entry (CHAT-SERVICE.md §11): its Channel's row is made from <c>GetPackage</c> when unknown (topics are
    /// unordered against each other), then the index applies it; a new visible message that arrived live gets a
    /// <c>message</c> event, a withdrawal a <c>withdrawn</c> event.
    /// </summary>
    /// <param name="entry">The Entry.</param>
    /// <param name="advance">It came from the Host's stream: the index records its store sequence.</param>
    /// <param name="live">It arrived while connected, or this service wrote it: events are emitted.</param>
    /// <param name="cancellationToken">Cancelled when the service stops.</param>
    /// <returns>What the index did.</returns>
    public async Task<IndexResult> TakeInAsync(Entry entry, bool advance, bool live, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ChannelRow? channel = await EnsureChannelAsync(entry.PackageId, cancellationToken).ConfigureAwait(false);
        if (channel is null)
        {
            if (advance)
                await Index.AdvanceAsync(entry.StoreSeq, cancellationToken).ConfigureAwait(false);
            return IndexResult.Nothing;
        }

        ChannelOwner owner = await Directory.OwnerAsync(new UserId(RowBytes.Hash(channel.OwnerId)), entry.DeviceId, cancellationToken).ConfigureAwait(false);
        IndexResult result = await Index.IndexAsync(entry, owner, advance, cancellationToken).ConfigureAwait(false);
        if (!live)
            return result;
        if (result is { Change: IndexChange.NewMessage, Message: { } message } && Timeline.Visible(message.Redacted, message.Broken))
        {
            AuthorView author = await Directory.AuthorAsync(entry.DeviceId, cancellationToken).ConfigureAwait(false);
            await EmitAsync(ChatEvents.Message, entry.PackageId, RowViews.Event(message, author), cancellationToken).ConfigureAwait(false);
        }
        else if (result is { Change: IndexChange.Withdrew, Message: { } withdrawn })
        {
            var gone = new WithdrawnEvent { Key = ChatIndex.KeyOf(withdrawn), PackageId = entry.PackageId, By = result.By };
            await EmitAsync(ChatEvents.Withdrawn, entry.PackageId, gone, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Applies a Package change (LOCAL-SPEC.md §7.3, CHAT-SERVICE.md §11): Followed makes or updates the row and rebuilds it
    /// from the history when rows are missing; a new Manifest renames; Unfollowed keeps the rows; Dropped removes them.
    /// </summary>
    /// <param name="changed">The change.</param>
    /// <param name="cancellationToken">Cancelled when the service stops.</param>
    /// <returns>A task that completes when it is applied.</returns>
    public async Task PackageChangedAsync(PackageChanged changed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changed);
        Package package = changed.Package;
        if (package.Kind != PackageKind.Channel)
            return;
        switch (changed.Change)
        {
            case PackageChange.Followed:
                await FollowedAsync(package, cancellationToken).ConfigureAwait(false);
                break;
            case PackageChange.Manifest when package.State == PackageState.Followed || await Index.ChannelAsync(package.PackageId, cancellationToken).ConfigureAwait(false) is not null:
                ChannelUpsert renamed = await Index.UpsertChannelAsync(package, cancellationToken).ConfigureAwait(false);
                if (renamed.Renamed)
                    await EmitChannelAsync(ChannelChange.Renamed, renamed.Row, cancellationToken).ConfigureAwait(false);
                break;
            case PackageChange.Unfollowed when await Index.ChannelAsync(package.PackageId, cancellationToken).ConfigureAwait(false) is not null:
                ChannelUpsert unfollowed = await Index.UpsertChannelAsync(package with { State = PackageState.Listed }, cancellationToken).ConfigureAwait(false);
                await EmitChannelAsync(ChannelChange.Unfollowed, unfollowed.Row, cancellationToken).ConfigureAwait(false);
                break;
            case PackageChange.Dropped:
                if (await Index.DropChannelAsync(package.PackageId, cancellationToken).ConfigureAwait(false) is { } dropped)
                    await EmitChannelAsync(ChannelChange.Dropped, dropped, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>Emits an event of the namespace (LOCAL-SPEC.md §9.10); a refusal is logged, never thrown.</summary>
    /// <typeparam name="T">The event's record.</typeparam>
    /// <param name="name">The event's name.</param>
    /// <param name="package">The Channel it concerns, followed; zero for none.</param>
    /// <param name="record">The event.</param>
    /// <param name="cancellationToken">Stops waiting for the Host's answer.</param>
    /// <returns>A task that completes when the Host answered.</returns>
    public async Task EmitAsync<T>(string name, PackageId package, T record, CancellationToken cancellationToken)
        where T : class, IWireRecord<T>
    {
        try
        {
            LocalResult<NoContent> emitted = await Service.EmitAsync(name, package, ChatCodec.Encode(record), cancellationToken).ConfigureAwait(false);
            if (!emitted.IsOk)
                ChatLog.NotEmitted(Service.Log, name, $"{emitted.Status} {emitted.Error?.Message}");
        }
        catch (LocalConnectionException ex)
        {
            ChatLog.NotEmitted(Service.Log, name, ex.FriendlyMessage);
        }
    }

    /// <summary>The connection ended: jobs on it stop.</summary>
    public void End() => ending.Cancel();

    /// <inheritdoc />
    public override string ToString() => $"chat session live from store seq {LiveFrom}";

    /// <summary>
    /// A Channel's row, made from <c>GetPackage</c> when the index does not know it yet: the topics are unordered against each
    /// other and against calls, so an Entry or a call may come before the Channel's <c>PackageChanged</c>.
    /// </summary>
    /// <param name="package">The Package.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The row; <see langword="null"/> when the Host knows no such Channel.</returns>
    public async Task<ChannelRow?> EnsureChannelAsync(PackageId package, CancellationToken cancellationToken)
    {
        if (await Index.ChannelAsync(package, cancellationToken).ConfigureAwait(false) is { } known)
            return known;
        LocalResult<Package> read = await Client.SendAsync(new GetPackage { PackageId = package }, cancellationToken).ConfigureAwait(false);
        if (!read.IsOk || read.Value.Kind != PackageKind.Channel)
            return null;
        ChannelUpsert made = await Index.UpsertChannelAsync(read.Value, cancellationToken).ConfigureAwait(false);
        await EmitChannelAsync(ChannelChange.Followed, made.Row, cancellationToken).ConfigureAwait(false);
        return made.Row;
    }

    private async Task FollowedAsync(Package package, CancellationToken cancellationToken)
    {
        ChannelUpsert upsert = await Index.UpsertChannelAsync(package, cancellationToken).ConfigureAwait(false);
        if (upsert.Created || upsert.FollowChanged)
            await EmitChannelAsync(ChannelChange.Followed, upsert.Row, cancellationToken).ConfigureAwait(false);
        if (upsert.Row.Indexed)
            return;

        // Rows are missing when the Host holds more Entries than the index has messages: take in the history.
        if (package.Entries > upsert.Row.Messages)
            await RebuildAsync(package.PackageId, cancellationToken).ConfigureAwait(false);
        if (await Index.MarkIndexedAsync(package.PackageId, true, cancellationToken).ConfigureAwait(false) is { } indexed)
            await EmitChannelAsync(ChannelChange.Indexed, indexed, cancellationToken).ConfigureAwait(false);
    }

    // Every Channel the Host lists: followed ones made or updated, rows of Channels no longer listed removed.
    private async Task ReconcileAsync(bool rebuild, CancellationToken cancellationToken)
    {
        var listed = new HashSet<PackageId>();
        var packages = new List<Package>();
        PackageId? after = null;
        while (true)
        {
            LocalResult<PackagePage> page = await Client.SendAsync(new ListPackages { Kinds = [PackageKind.Channel], After = after }, cancellationToken).ConfigureAwait(false);
            if (!page.IsOk)
                return;
            packages.AddRange(page.Value.Packages);
            if (!page.Value.More || page.Value.Packages.Count == 0)
                break;
            after = page.Value.Packages[^1].PackageId;
        }

        Dictionary<PackageId, ChannelRow> rows = (await Index.ChannelsAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(r => new PackageId(RowBytes.Hash(r.PackageId)));
        foreach (Package package in packages)
        {
            listed.Add(package.PackageId);
            if (package.State != PackageState.Followed && !rows.ContainsKey(package.PackageId))
                continue;
            ChannelUpsert upsert = await Index.UpsertChannelAsync(package, cancellationToken).ConfigureAwait(false);
            if (package.State != PackageState.Followed)
                continue;
            if (rebuild)
                await RebuildAsync(package.PackageId, cancellationToken).ConfigureAwait(false);
            if (!upsert.Row.Indexed)
                await Index.MarkIndexedAsync(package.PackageId, true, cancellationToken).ConfigureAwait(false);
        }

        foreach (PackageId gone in rows.Keys.Where(id => !listed.Contains(id)))
            await Index.DropChannelAsync(gone, cancellationToken).ConfigureAwait(false);
    }

    // The Channel's history as the Host holds it (LOCAL-SPEC.md §6.4 ListHistory), each Entry taken in; replays change nothing.
    private async Task RebuildAsync(PackageId package, CancellationToken cancellationToken)
    {
        EntryKey? after = null;
        while (true)
        {
            var request = new ListHistory { PackageId = package, After = after, Kinds = Kinds, Limit = (ushort)Limits.HistoryPageEntries };
            LocalResult<EntryPage> page = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!page.IsOk)
                return;
            foreach (Entry entry in page.Value.Entries)
                await TakeInAsync(entry, advance: false, live: false, cancellationToken).ConfigureAwait(false);
            if (!page.Value.More || page.Value.Entries.Count == 0)
                return;
            after = page.Value.Entries[^1].Key;
        }
    }

    private async Task EmitChannelAsync(ChannelChange change, ChannelRow row, CancellationToken cancellationToken)
    {
        string owner = await Directory.NameOfAsync(new UserId(RowBytes.Hash(row.OwnerId)), cancellationToken).ConfigureAwait(false);
        ChannelView view = RowViews.Channel(row, owner);

        // A Channel no longer followed is no Package the Host lets the service emit for (LOCAL-SPEC.md §9.10): zero instead.
        PackageId package = row.Followed && change is not (ChannelChange.Unfollowed or ChannelChange.Dropped) ? view.PackageId : default;
        await EmitAsync(ChatEvents.Channel, package, new ChannelEvent { Change = change, Channel = view }, cancellationToken).ConfigureAwait(false);
    }
}
