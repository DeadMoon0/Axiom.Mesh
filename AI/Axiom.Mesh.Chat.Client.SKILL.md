<identity>
    <package>Axiom.Mesh.Chat.Client</package>
    <role>addon-skill</role>
    <requires>Axiom.Mesh.Local.SKILL.md</requires>
</identity>

<objective>
    Teach an agent to write an app (or app commands) over the chat service's namespace axiom-mesh-chat/1: call its methods with ChatCall over ServiceCall, read its events with ChatEventReader, and explain its statuses, without ever referencing the service's executable.
</objective>

<package_scope>
    Namespace Axiom.Mesh.Chat. ChatProtocol { ServiceName "chat", Namespace "axiom-mesh-chat/1" }. ChatMethods (about, channels, timeline, message, thread, send, withdraw, attach, get, jobs) and ChatEvents (message, withdrawn, channel, job): const string tables. One record per request, reply and event: AboutRequest/AboutReply { Namespace, Version, StoreEpoch, IndexedSeq, CaughtUp, Channels }, ChannelsRequest/ChannelsReply { Channels: ChannelView }, TimelineRequest { PackageId, After: EntryKey?, Reverse, Limit 1-256 }/TimelineReply { Messages, More }, MessageRequest { Hash }/MessageReply { Message }, ThreadRequest { Message, Limit }/ThreadReply { Root, Replies, More }, SendRequest { PackageId, Text <=16384 bytes, Attachments <=64 (Axiom.Mesh.Payloads.Attachment), ReplyTo }/SendReply { Key }, WithdrawRequest { Message }/WithdrawReply { Key }, AttachRequest { Path absolute, Name }/AttachReply { Job }, GetRequest { Hash, Message, Path absolute, Existing }/GetReply { Job }, JobsRequest/JobsReply { Jobs }. Views: AuthorView { DeviceId, UserId, UserName, DeviceName }, ChannelView { PackageId, Name, OwnerId, OwnerName, Followed, Messages, Withdrawn, Last?, Indexed }, MessageView { Key, PackageId, Author, Stored, Text, Attachments, ReplyTo, Withdrawn, WithdrawnBy, Broken }, JobView { Job, Kind (JobKind Attach|Get), State (JobState Running|Done|Failed), Done, Total, Message, Status, Attachment?, Path, Problem }. Events derive from ChatEvent (closed set, Name): MessageEvent { Key, PackageId, Author, Preview <=256 bytes, Attachments count, ReplyTo }, WithdrawnEvent { Key, PackageId, By }, ChannelEvent { Change (ChannelChange Indexed|Followed|Unfollowed|Dropped|Renamed), Channel }, JobEvent { Job }. IChatMethod<TSelf, TReply> (static Method) pairs a request with its reply. ChatCall.SendAsync(LocalClient user, request, ct) -> LocalResult<TReply>. ChatEventReader.TryRead(ServiceEvent, out ChatEvent) and FilterOf(PackageId) for topic service. ChatStatus { None 0, NotAChannel 32768, NotAMessage, NotYours, AttachmentMissing, Withdrawn, JobNotFound 32773 }, ChatStatusText.IsChatStatus(Status, out ChatStatus) and Explain(ChatStatus). ChatLimits (TimelinePage 256, PreviewLength 256, MaxJobs 16, EndedJobsKept 48, JobsListed 64, Channels 256, PayloadBytes 1 MiB, TextLength, Attachments, AttachmentNameLength, PathLength, ThreadDepth 64). ChatCodec.Encode/TryDecode/SizeOf for any record.
</package_scope>

<architecture>
    Every method is one ServiceCall to service chat: the request's payload, the reply's payload, or a status (LOCAL-SPEC.md §11.1, or a ChatStatus from 32768) with the service's Error. ChatCall maps a reply that does not parse to ServiceUnavailable. Events arrive on topic service; a filter with a Package receives that Channel's message, withdrawn and channel events; job events carry the zero Package, so a waiter subscribes with a zero filter. Records are SPEC.md §5.2 types; EntryKey is clock, deviceId, hash; optional is a bool then the value. CHAT-SERVICE.md is the specification; Documentation/vectors/chat/records.txt holds a payload of every record.
</architecture>

<best_practices>
    Subscribe to topic service before starting a job (attach, get), then wait for the JobEvent with the job's number and state Done or Failed; on a lapse ask jobs. Wait for about.CaughtUp before trusting a timeline to be complete, never for time. Page a timeline with After = the last message's Key while More. Show (withdrawn) or (broken) for such messages; their Text and Attachments are empty by contract. Explain statuses with ChatStatusText for codes from 32768 and your own text otherwise; ServiceUnavailable means the chat service is not connected (axiom-mesh services shows it).
</best_practices>

<pitfalls>
    send and withdraw are not idempotent: after a ServiceTimeout look at the timeline before sending again. Paths in attach and get are the service's: absolute, read and written as the Host's user. Merge is for folders only (a ChunkList attachment written as a file takes Fail or Replace). MessageView.Stored is the local Host's time: compare devices by Key, not by Stored. ChatEvent is a closed set; an unknown event name (a later namespace version) reads as false, ignore it.
</pitfalls>
