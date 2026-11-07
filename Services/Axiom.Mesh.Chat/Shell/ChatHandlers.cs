using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The methods of <c>axiom-mesh-chat/1</c> (CHAT-SERVICE.md §5) on one connection: each decodes its request, asks the rules,
/// reads or writes through the index and the Host, and answers its reply or a status. Long work is a job (S-D8); a call
/// always answers within its deadline.
/// </summary>
internal sealed class ChatHandlers
{
    private const int ThreadCap = 4_096;
    private const int UnpinBatch = 1_024;

    private readonly ChatSession session;

    /// <summary>The handlers of a connection.</summary>
    /// <param name="session">The connection.</param>
    public ChatHandlers(ChatSession session) => this.session = session;

    /// <summary>Answers one call.</summary>
    /// <param name="call">The forwarded call.</param>
    /// <param name="deadline">Cancelled at 60 seconds and when the connection ends.</param>
    /// <returns>The reply.</returns>
    public async ValueTask<CallReply> CallAsync(ForwardedCall call, CancellationToken deadline)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call.Method switch
        {
            ChatMethods.About => await RunAsync<AboutRequest, AboutReply>(call, AboutAsync, deadline).ConfigureAwait(false),
            ChatMethods.Channels => await RunAsync<ChannelsRequest, ChannelsReply>(call, ChannelsAsync, deadline).ConfigureAwait(false),
            ChatMethods.Timeline => await RunAsync<TimelineRequest, TimelineReply>(call, TimelineAsync, deadline).ConfigureAwait(false),
            ChatMethods.Message => await RunAsync<MessageRequest, MessageReply>(call, MessageAsync, deadline).ConfigureAwait(false),
            ChatMethods.Thread => await RunAsync<ThreadRequest, ThreadReply>(call, ThreadAsync, deadline).ConfigureAwait(false),
            ChatMethods.Send => await RunAsync<SendRequest, SendReply>(call, SendAsync, deadline).ConfigureAwait(false),
            ChatMethods.Withdraw => await RunAsync<WithdrawRequest, WithdrawReply>(call, WithdrawAsync, deadline).ConfigureAwait(false),
            ChatMethods.Attach => await RunAsync<AttachRequest, AttachReply>(call, AttachAsync, deadline).ConfigureAwait(false),
            ChatMethods.Get => await RunAsync<GetRequest, GetReply>(call, GetAsync, deadline).ConfigureAwait(false),
            ChatMethods.Jobs => await RunAsync<JobsRequest, JobsReply>(call, JobsAsync, deadline).ConfigureAwait(false),
            _ => CallReply.Failure(Status.Invalid, ErrorText.Of($"The chat service has no method '{call.Method}' in {ChatProtocol.Namespace}.", "method")),
        };
    }

    /// <inheritdoc />
    public override string ToString() => "chat handlers of " + session;

    private static async Task<CallReply> RunAsync<TRequest, TReply>(ForwardedCall call, Func<TRequest, CancellationToken, Task<Answer<TReply>>> handle, CancellationToken deadline)
        where TRequest : class, IChatMethod<TRequest, TReply>
        where TReply : class, IWireRecord<TReply>
    {
        if (!ChatCodec.TryDecode(call.Payload, out TRequest? request))
            return CallReply.Failure(Status.Invalid, ErrorText.Of($"The payload is no {typeof(TRequest).Name} of {ChatProtocol.Namespace}.", "payload"));
        Answer<TReply> answer = await handle(request, deadline).ConfigureAwait(false);
        return answer.Reply is { } reply ? CallReply.Ok(ChatCodec.Encode(reply)) : answer.Refusal!;
    }

    private async Task<Answer<AboutReply>> AboutAsync(AboutRequest request, CancellationToken deadline)
    {
        IndexMeta meta = await session.Index.ReadMetaAsync(deadline).ConfigureAwait(false);
        LocalResult<EntryPage> beyond = await session.Client.SendAsync(new ListStored { Kinds = session.Service.Definition.EntryKinds, FromSeq = meta.LastSeq + 1, Limit = 1 }, deadline).ConfigureAwait(false);
        if (!beyond.IsOk)
            return ErrorText.Passed(beyond.Status, beyond.Error);
        List<ChannelRow> channels = await session.Index.ChannelsAsync(deadline).ConfigureAwait(false);
        return new AboutReply
        {
            Namespace = ChatProtocol.Namespace,
            Version = Timeline.Preview(session.Service.Definition.Version, ChatLimits.NameLength),
            StoreEpoch = meta.StoreEpoch,
            IndexedSeq = meta.LastSeq,
            CaughtUp = beyond.Value.Entries.Count == 0,
            Channels = (uint)channels.Count,
        };
    }

    private async Task<Answer<ChannelsReply>> ChannelsAsync(ChannelsRequest request, CancellationToken deadline)
    {
        List<ChannelRow> rows = await session.Index.ChannelsAsync(deadline).ConfigureAwait(false);
        var views = new List<ChannelView>(rows.Count);
        foreach (ChannelRow row in rows)
            views.Add(RowViews.Channel(row, await session.Directory.NameOfAsync(new UserId(RowBytes.Hash(row.OwnerId)), deadline).ConfigureAwait(false)));
        return new ChannelsReply { Channels = [.. views.OrderBy(v => v.Name, StringComparer.Ordinal).ThenBy(v => v.PackageId.Value).Take(ChatLimits.Channels)] };
    }

    private async Task<Answer<TimelineReply>> TimelineAsync(TimelineRequest request, CancellationToken deadline)
    {
        if (request.Limit is < 1 or > ChatLimits.TimelinePage)
            return ErrorText.Refused(Verdict.Refuse(Status.Invalid, $"A timeline page holds 1 to {ChatLimits.TimelinePage} messages.", "limit"));
        if (await session.EnsureChannelAsync(request.PackageId, deadline).ConfigureAwait(false) is null)
            return ErrorText.Refused(Verdict.Refuse(ChatStatus.NotAChannel, "The service knows no such Channel.", "packageId"));
        List<MessageRow> rows = await session.Index.PageAsync(request.PackageId, request.After, request.Reverse, request.Limit + 1, deadline).ConfigureAwait(false);
        IReadOnlyList<MessageView> views = await ViewsAsync(rows, deadline).ConfigureAwait(false);
        return TimelinePage.Cut(views, request.After, request.Reverse, request.Limit, ChatLimits.PayloadBytes);
    }

    private async Task<Answer<MessageReply>> MessageAsync(MessageRequest request, CancellationToken deadline)
    {
        if (await session.Index.MessageAsync(request.Hash, deadline).ConfigureAwait(false) is not { } row)
            return ErrorText.Refused(Verdict.Refuse(ChatStatus.NotAMessage, "No such message is indexed.", "hash"));
        return new MessageReply { Message = (await ViewsAsync([row], deadline).ConfigureAwait(false))[0] };
    }

    private async Task<Answer<ThreadReply>> ThreadAsync(ThreadRequest request, CancellationToken deadline)
    {
        if (request.Limit is < 1 or > ChatLimits.TimelinePage)
            return ErrorText.Refused(Verdict.Refuse(Status.Invalid, $"A thread page holds 1 to {ChatLimits.TimelinePage} replies.", "limit"));
        List<MessageRow> chain = await session.Index.ChainAsync(request.Message, ChatLimits.ThreadDepth, deadline).ConfigureAwait(false);
        if (chain.Count == 0)
            return ErrorText.Refused(Verdict.Refuse(ChatStatus.NotAMessage, "No such message is indexed.", "message"));
        ObjectHash root = Threading.RootOf(request.Message, ReplyMap(chain), ChatLimits.ThreadDepth);
        MessageRow rootRow = chain.First(r => RowBytes.Hash(r.Hash) == root.Value);
        List<MessageRow> descendants = await session.Index.DescendantsAsync(root, ThreadCap, deadline).ConfigureAwait(false);
        IReadOnlySet<ObjectHash> replies = Threading.Replies(root, ReplyMap([rootRow, .. descendants]));
        IReadOnlyList<MessageView> views = await ViewsAsync([rootRow, .. Timeline.Order(descendants.Where(r => replies.Contains(new ObjectHash(RowBytes.Hash(r.Hash)))), ChatIndex.KeyOf, reverse: false)], deadline).ConfigureAwait(false);
        MessageView rootView = views[0];
        TimelineReply page = TimelinePage.Cut([.. views.Skip(1)], null, reverse: false, request.Limit, ChatLimits.PayloadBytes - ChatCodec.SizeOf(rootView));
        return new ThreadReply { Root = rootView, Replies = page.Messages, More = page.More };
    }

    private async Task<Answer<SendReply>> SendAsync(SendRequest request, CancellationToken deadline)
    {
        ChannelRow? channel = await session.EnsureChannelAsync(request.PackageId, deadline).ConfigureAwait(false);
        MessageRow? answered = request.ReplyTo.IsZero ? null : await session.Index.MessageAsync(request.ReplyTo, deadline).ConfigureAwait(false);
        bool replyInChannel = answered is not null && RowBytes.Hash(answered.Package) == request.PackageId.Value;
        Verdict verdict = SendRule.Check(request, channel is { Followed: true }, replyInChannel);
        if (!verdict.IsPass)
            return ErrorText.Refused(verdict);

        var payload = new MessagePayload { Text = request.Text, Attachments = request.Attachments, ReplyTo = request.ReplyTo };
        LocalResult<Entry> written = await session.Client.SendAsync(WriteEntry.Of(request.PackageId, payload.ToBody()), deadline).ConfigureAwait(false);
        if (!written.IsOk)
        {
            // LOCAL-SPEC.md §9.4: a referenced object not stored is NotFound, and the Channel is followed: an attachment is missing.
            return written.Status == Status.NotFound && request.Attachments.Count > 0
                ? ErrorText.Refused(Verdict.Refuse(ChatStatus.AttachmentMissing, written.Error?.Message is { Length: > 0 } why ? why : "An attachment is not stored here.", "attachments"))
                : ErrorText.Passed(written.Status, written.Error);
        }

        // LOCAL-SPEC.md §9.5: put, write the Entry, then unpin; the Outbound pins keep what no peer has yet.
        IReadOnlyList<ObjectHash> pinned = await session.Index.TakePendingAsync([.. request.Attachments.Select(a => a.Hash)], CancellationToken.None).ConfigureAwait(false);
        foreach (ObjectHash[] batch in pinned.Chunk(UnpinBatch))
            await session.Client.SendAsync(new Unpin { Hashes = batch }, CancellationToken.None).ConfigureAwait(false);
        await session.TakeInAsync(written.Value, advance: false, live: true, CancellationToken.None).ConfigureAwait(false);
        return new SendReply { Key = written.Value.Key };
    }

    private async Task<Answer<WithdrawReply>> WithdrawAsync(WithdrawRequest request, CancellationToken deadline)
    {
        // The identity as it stands, not the HelloAck's: the service may have been spawned before identity create.
        if (session.Service.Identity is not { } own)
            return ErrorText.Refused(Verdict.Refuse(ChatStatus.NotYours, "This Host has no identity yet, so none of its devices wrote or owns anything; create one with axiom-mesh identity create.", "message"));
        MessageRow? target = await session.Index.MessageAsync(request.Message, deadline).ConfigureAwait(false);
        ChannelRow? channel = target is null ? null : await session.Index.ChannelAsync(new PackageId(RowBytes.Hash(target.Package)), deadline).ConfigureAwait(false);
        Verdict verdict = WithdrawRule.Check(
            target is not null && channel is not null,
            target?.Redacted ?? false,
            new DeviceId(RowBytes.Hash(target?.Device)),
            new UserId(RowBytes.Hash(channel?.OwnerId)),
            own.DeviceId,
            own.UserId);
        if (!verdict.IsPass)
            return ErrorText.Refused(verdict);

        var payload = new RedactionPayload { Target = request.Message };
        LocalResult<Entry> written = await session.Client.SendAsync(WriteEntry.Of(new PackageId(RowBytes.Hash(target!.Package)), payload.ToBody()), deadline).ConfigureAwait(false);
        if (!written.IsOk)
            return ErrorText.Passed(written.Status, written.Error);
        await session.TakeInAsync(written.Value, advance: false, live: true, CancellationToken.None).ConfigureAwait(false);
        return new WithdrawReply { Key = written.Value.Key };
    }

    private async Task<Answer<AttachReply>> AttachAsync(AttachRequest request, CancellationToken deadline)
    {
        Verdict where = AttachmentRule.ForAttach(request.Path, File.Exists(request.Path), Directory.Exists(request.Path), out AttachmentKind kind);
        if (!where.IsPass)
            return ErrorText.Refused(where);
        Verdict named = AttachmentRule.NameOf(request.Name, request.Path, out string name);
        if (!named.IsPass)
            return ErrorText.Refused(named);
        if (!session.Jobs.TryStart(new JobView { Kind = JobKind.Attach, Path = request.Path }, out JobView job))
            return ErrorText.Refused(Verdict.Refuse(Status.Busy, $"{ChatLimits.MaxJobs} jobs are running; wait for one to end.", "path"));
        await ChatJobs.ReportAsync(session, job).ConfigureAwait(false);
        _ = Task.Run(() => ChatJobs.AttachAsync(session, job, kind, request.Path, name), CancellationToken.None);
        return new AttachReply { Job = job.Job };
    }

    private async Task<Answer<GetReply>> GetAsync(GetRequest request, CancellationToken deadline)
    {
        if (await session.Index.MessageAsync(request.Message, deadline).ConfigureAwait(false) is not { } message)
            return ErrorText.Refused(Verdict.Refuse(ChatStatus.NotAMessage, "No such message is indexed.", "message"));
        if (message.Redacted)
            return ErrorText.Refused(Verdict.Refuse(ChatStatus.Withdrawn, "The message is withdrawn; its attachments are not fetched.", "message"));
        Attachment? attachment = message.Broken ? null : AttachmentsBlob.Read(message.Attachments).Where(a => a.Hash == request.Hash).Select(a => (Attachment?)a).FirstOrDefault();
        if (attachment is not { } named)
            return ErrorText.Refused(Verdict.Refuse(ChatStatus.AttachmentMissing, "The message names no such attachment.", "hash"));
        Verdict path = AttachmentRule.ForGet(AttachmentKind.Folder, request.Existing, request.Path);
        if (!path.IsPass)
            return ErrorText.Refused(path);
        if (!session.Jobs.TryStart(new JobView { Kind = JobKind.Get, Message = request.Message, Path = request.Path, Attachment = named, Total = named.Size }, out JobView job))
            return ErrorText.Refused(Verdict.Refuse(Status.Busy, $"{ChatLimits.MaxJobs} jobs are running; wait for one to end.", "path"));
        await ChatJobs.ReportAsync(session, job).ConfigureAwait(false);
        var package = new PackageId(RowBytes.Hash(message.Package));
        _ = Task.Run(() => ChatJobs.GetAsync(session, job, package, named, request.Path, request.Existing), CancellationToken.None);
        return new GetReply { Job = job.Job };
    }

    private Task<Answer<JobsReply>> JobsAsync(JobsRequest request, CancellationToken deadline) =>
        Task.FromResult<Answer<JobsReply>>(new JobsReply { Jobs = session.Jobs.List() });

    private static Dictionary<ObjectHash, ObjectHash> ReplyMap(IEnumerable<MessageRow> rows)
    {
        var map = new Dictionary<ObjectHash, ObjectHash>();
        foreach (MessageRow row in rows)
            map[new ObjectHash(RowBytes.Hash(row.Hash))] = new ObjectHash(RowBytes.Hash(row.ReplyTo));
        return map;
    }

    private async Task<IReadOnlyList<MessageView>> ViewsAsync(IReadOnlyList<MessageRow> rows, CancellationToken deadline)
    {
        var authors = new Dictionary<DeviceId, AuthorView>();
        var views = new List<MessageView>(rows.Count);
        foreach (MessageRow row in rows)
        {
            var device = new DeviceId(RowBytes.Hash(row.Device));
            if (!authors.TryGetValue(device, out AuthorView? author))
                authors[device] = author = await session.Directory.AuthorAsync(device, deadline).ConfigureAwait(false);
            views.Add(RowViews.Message(row, author));
        }

        return views;
    }
}
