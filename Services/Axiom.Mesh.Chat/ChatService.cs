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
    private (ServiceSession Session, TaskCompletionSource<ChatSession> Ready)? current;
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
        TaskCompletionSource<ChatSession> ready = ReadyOf(session);
        try
        {
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

            // Calls that arrived while the index was brought in step (the runtime serves them from HelloAck on) go ahead now.
            ready.TrySetResult(made);
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task OnEntryAsync(ServiceSession session, EntryStored stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ChatSession chat = await ReadyOf(session).Task.ConfigureAwait(false);
        await chat.TakeInAsync(stored.Entry, advance: true, live: stored.Entry.StoreSeq > chat.LiveFrom, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnPackageChangedAsync(ServiceSession session, PackageChanged changed, CancellationToken cancellationToken)
    {
        ChatSession chat = await ReadyOf(session).Task.ConfigureAwait(false);
        await chat.PackageChangedAsync(changed, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<CallReply> OnCallAsync(ServiceSession session, ForwardedCall call, CancellationToken deadline)
    {
        // A call may come before OnConnectedAsync finished bringing the index in step; it waits for it, within its deadline.
        ChatSession chat = await ReadyOf(session).Task.WaitAsync(deadline).ConfigureAwait(false);
        return await new ChatHandlers(chat).CallAsync(call, deadline).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task OnDisconnectedAsync(ServiceSession session, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (current is { } now && now.Session == session)
            {
                if (now.Ready.Task.IsCompletedSuccessfully)
                    now.Ready.Task.Result.End();
                now.Ready.TrySetCanceled(CancellationToken.None);
                current = null;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => $"chat service, {jobs}";

    // The chat session of a connection once it is ready; the first to ask (a call or OnConnectedAsync) makes the slot.
    private TaskCompletionSource<ChatSession> ReadyOf(ServiceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (gate)
        {
            if (current is { } now && now.Session == session)
                return now.Ready;
            var ready = new TaskCompletionSource<ChatSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            current = (session, ready);
            return ready;
        }
    }
}
