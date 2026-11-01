using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The chat service (CHAT-SERVICE.md): it defines the Channel kind with Message and Redaction, keeps an index of every
/// Channel this Host follows in its own database, and offers it to every app as the namespace <c>axiom-mesh-chat/1</c>,
/// methods over <c>ServiceCall</c> and events over <c>Emit</c>. It owns no user interface (S-D13). Run by
/// <see cref="ServiceProgram"/>: spawned by the Host as <c>axiom-mesh-chat</c>, or in process beside a test node.
/// </summary>
public sealed class ChatService : IMeshService
{
    private const int UnpinBatch = 1_024;

    private readonly TimeProvider time;
    private readonly JobBook jobs;
    private readonly object gate = new();
    private ChatIndex? index;
    private (ServiceSession Session, ChatSession Chat)? current;
    private bool pendingReleased;

    /// <summary>The chat service on the system's clock.</summary>
    public ChatService()
        : this(TimeProvider.System)
    {
    }

    /// <summary>The chat service on a clock: the jobs' progress is reported at most once a second of it.</summary>
    /// <param name="time">The clock; a test's fake one.</param>
    public ChatService(TimeProvider time)
    {
        this.time = time ?? throw new ArgumentNullException(nameof(time));
        jobs = new JobBook(time);
    }

    /// <inheritdoc />
    public ServiceDefinition Definition => ChatProgram.Definition;

    /// <inheritdoc />
    public async Task OnConnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        index ??= await ChatDatabase.OpenAsync(session.State.Database, cancellationToken).ConfigureAwait(false);
        var made = new ChatSession(session, index, jobs, time);
        await made.ConnectAsync(cancellationToken).ConfigureAwait(false);
        if (!pendingReleased)
        {
            // An attach whose send never came left its pins behind in an earlier run (LOCAL-SPEC.md §9.5, §9.6).
            IReadOnlyList<ObjectHash> stale = await index.TakeAllPendingAsync(cancellationToken).ConfigureAwait(false);
            foreach (ObjectHash[] batch in stale.Chunk(UnpinBatch))
                await session.Client.SendAsync(new Unpin { Hashes = batch }, cancellationToken).ConfigureAwait(false);
            if (stale.Count > 0)
                ChatLog.PendingReleased(session.Log, stale.Count);
            pendingReleased = true;
        }

        // Entries, Package changes and calls come only after this returned (the runtime holds calls back until then).
        lock (gate)
            current = (session, made);
    }

    /// <inheritdoc />
    public async Task OnEntryAsync(ServiceSession session, EntryStored stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ChatSession chat = ChatOf(session);
        await chat.TakeInAsync(stored.Entry, advance: true, live: stored.Entry.StoreSeq > chat.LiveFrom, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnEntriesAsync(ServiceSession session, IReadOnlyList<EntryStored> stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ChatSession chat = ChatOf(session);
        await chat.TakeInManyAsync([.. stored.Select(s => s.Entry)], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnPackageChangedAsync(ServiceSession session, PackageChanged changed, CancellationToken cancellationToken)
    {
        ChatSession chat = ChatOf(session);
        await chat.PackageChangedAsync(changed, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<CallReply> OnCallAsync(ServiceSession session, ForwardedCall call, CancellationToken deadline)
    {
        ChatSession chat = ChatOf(session);
        return await new ChatHandlers(chat).CallAsync(call, deadline).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task OnDisconnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (current is { } now && now.Session == session)
            {
                now.Chat.End();
                current = null;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"chat service, {jobs}";

    // The chat session of a connection; the runtime calls every handler but OnConnectedAsync only after that one returned.
    private ChatSession ChatOf(ServiceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (gate)
        {
            return current is { } now && now.Session == session
                ? now.Chat
                : throw new InvalidOperationException("The runtime handed the chat service an event of a session it was not connected on.");
        }
    }
}
