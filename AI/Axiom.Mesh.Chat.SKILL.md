<identity>
    <package>Axiom.Mesh.Chat</package>
    <role>addon-skill</role>
    <requires>Axiom.Mesh.Service.SKILL.md, Axiom.Mesh.Chat.Client.SKILL.md</requires>
</identity>

<objective>
    Teach an agent to change the chat service axiom-mesh-chat: its index of Channels and messages in its own LDB database, its pure rules, its method handlers and jobs, and how it is tested and shipped, keeping it a service without a user interface.
</objective>

<package_scope>
    Executable Services/Axiom.Mesh.Chat (namespace Axiom.Mesh.Chat; Native AOT TargetName axiom-mesh-chat with IlcArg --noscan, STRUCTURAL-DEBT 47; not packable). Public: ChatService : IMeshService (ctor() on the system clock, ctor(TimeProvider)), ChatProgram { Version, Definition (chat, Channel kind 2 with Message 2 and Redaction 3) }; Program.cs is one line over ServiceProgram.Run. Internal (open to Chat.Tests and Axiom.Mesh.Benchmarks): Rules/ (pure: Timeline Order/Later/Visible/Preview, TimelinePage.Cut(candidates, after, reverse, limit, byteBudget), Threading.RootOf/Replies, SendRule, WithdrawRule, RedactionApplication -> RedactionOutcome, AttachmentRule ForAttach/NameOf/SizeOf/ForGet, PayloadCheck OfMessage/RedactionIsBroken, CatchUpPlan.Decide -> IndexAction Keep|Discard|DiscardAndRebuild, JobRule MayStart/Remember/ShouldReport, AuthorResolution, Verdict), Index/ (MetaRow, ChannelRow, MessageRow, AttachmentRow, RedactionRow, PendingRow; ChatDatabase.OpenAsync(Database) and OpenDirectoryAsync; ChatIndex with IndexManyAsync/IndexAsync over IndexBatch, PageAsync, ChainAsync, DescendantsAsync, Upsert/Drop/MarkIndexed channel, pending pins; RowViews; RowBytes; AttachmentsBlob), Shell/ (ChatSession per connection: ConnectAsync, TakeInAsync, TakeInManyAsync, PackageChangedAsync, EnsureChannelAsync, EmitAsync; ChatHandlers per method; ChatJobs attach/get; JobBook; UserDirectory; ErrorText; ChatLog; Answer<T>).
</package_scope>

<architecture>
    The runtime connects, registers the Channel kind and hands over Entries in store order (pages of up to 64 to OnEntriesAsync, one index transaction each; live Entries one by one), Package changes and calls. OnConnectedAsync opens the index on session.State.Database, applies CatchUpPlan (discard on another epoch or schema; rebuild from ListHistory when the runtime resumes past lost rows), reconciles Channels with ListPackages(kind Channel), and releases pending attachment pins of earlier runs; calls wait for it (a call can arrive before). Index writes of Entries and the runtime cursor commit with Durability.Lazy (a crash loses a suffix, in order), pending pins with Full. Every Entry write dedupes by hash; a redaction is any Entry with redacts not zero, kept and applied by Redactions.Honours in both orders; a Message whose envelope does not match its payload is broken. send = SendRule, WriteEntry.Of(pkg, MessagePayload.ToBody()), Unpin the attachment objects (ChatPending), index the Entry at once, emit message. withdraw = WithdrawRule (NotYours before writing), WriteEntry of RedactionPayload. attach/get are jobs (S-D8): PutFileAsync or PublishDirectoryAsync then ChatPending; Fetch(recursive) with topic operations subscribed first, then MaterialiseFileAsync or MaterialiseAsync. Events: message only for live Entries (store seq above HelloAck.StoreSeq) or sends; channel events of unfollowed or dropped Channels carry the zero Package.
</architecture>

<best_practices>
    Keep decisions in Rules/ with a test from CHAT-SERVICE.md per point (Chat.Tests: Rules/); keep handlers thin. Add a method by adding its records to Chat.Client (ChatMethods, request, reply), a row to CHAT-SERVICE.md §5 (ChatServiceDocTests), a case to ChatHandlers.CallAsync (MethodTableTests) and a sample to Chat.Client.Tests ChatSamples plus the vector file (AXIOM_MESH_WRITE_VECTORS=1). Scenarios go into ChatScenarios (one file, run in memory and on sockets); wait with about.CaughtUp, never time. Ship through Scripts/aot-smoke.* and package.*, which publish it beside axiom-meshd.
</best_practices>

<pitfalls>
    No System.Console outside Program.cs, no reference to the CLI or a UI framework (ServiceConventionTests). Error.Message is at most 256 bytes: build errors with ErrorText. Emit for a Package that is not followed is NotFound. One LdbContext per write through the index's gate; IndexBatch keeps the rows it read or added, because a redaction and its target can meet in one page. TestNode/RealNode state per service name: two runs of the service must not hold one state at once.
</pitfalls>
