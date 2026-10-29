<identity>
    <package>Axiom.Mesh.Core</package>
    <role>core-skill</role>
    <requires>Axiom.Mesh.Model.SKILL.md, Axiom.Mesh.Protocol.SKILL.md, Axiom.Mesh.Local.SKILL.md</requires>
</identity>

<objective>
    Teach an agent the engine of an Axiom.Mesh node: its store and index, its trust, its single writer and intake, and the thread-safety model they share.
</objective>

<package_scope>
    Namespaces Axiom.Mesh.Core and Axiom.Mesh.Core.Storage. Public (what the Host and Testing plug in): MeshNode (not startable yet), MeshStoreException, IEntropy with SystemEntropy and DeterministicEntropy, IObjectFiles with DirectoryObjectFiles (layout objects/h01/h23/hash, temp + flush + rename, delete-shared reads) and InMemoryObjectFiles, ObjectDeletion. Internal, built in M5: the LDB entities (UserRow, DeviceRow, PackageRow, GrantRow, HeadRow, EntryRow with unique StoreSeq and the Clock index, ObjectRow, PinRow, PeerRow, MetaRow, ReferenceRow) and MeshDatabase (tables, store.epoch made once, schema.version checked); TrustSnapshot (immutable ITrustSnapshotView; TryResolveDevice(point) for the TLS callback; IsFollowed, IsKnownUser, IsRemoved, MayList, MayServeGrant, MayServeRoster) and TrustBuilder; StoreWriter (bounded queue of StoreCommands: AdmitCommand, PackageCommand, PinCommand, IdentityCommand, PairCommand, PeerCommand, TouchCommand, EvictCommand, EvictUnderBudgetCommand) with StoreBatch and PinBook; ChangeBus, ChangeSubscription, ChangeSet; Intake, IntakeRules, HeldBuffer, IWantedSet; ObjectStore, ObjectHandle, Promises, Evictor, EntryIndex, RetentionRule, ChunkStreamer. Planned (M6 to M9): MeshNode.StartAsync with MeshNodeEnvironment, MeshTls, PeerTable, replication, LocalPort and the request dispatcher.
</package_scope>

<architecture>
    One writer, immutable snapshots, per-session actors (plan decision D8). StoreWriter alone writes the object files and the index: it drains up to 256 commands or 8 MiB of object bytes, writes new object files first, applies everything in one LDB WriteTransaction (reads inside it see its own writes), swaps in a new TrustSnapshot when trust changed, publishes the ChangeSet, and only then completes the commands; a failed batch fails all of them and reloads WriterState from the database. Intake runs on the caller's thread: copy (signed objects into own arrays, the rest into a node-owned pool), hash, parse, IntakeRules.Decide against writer.Trust, then admit or hold; it awaits Rosters, Manifests and Grants and re-examines the held buffer after each. The total order is paged through the ordered Clock index, ties of one clock ordered in memory (LDB 0.1.0 sorts any byte-string order in memory). Depends on Model, Protocol, Local, Discovery, ProtoStream, LDB (Entity, Core, Primitives, Query) and, once published, LWSE 0.3.0.
</architecture>

<best_practices>
    Store only through AdmitCommand with a StorableObject; for objects from peers go through Intake. Await a command when its effect matters; post TouchCommand without waiting. Use writer.FlushAsync in tests as a barrier, never a sleep. Pass timestamps into HeldBuffer and pure rules; the writer takes its clock from the TimeProvider. Subscribe to the ChangeBus with a bounded capacity and treat Lapsed as "re-read the state". Clients pin only with PinReason.User or Service. Every limit comes from Model's Limits.
</best_practices>

<pitfalls>
    A parsed object refers to the memory it was parsed from: copy a frame before parsing, and never hand the writer a pooled buffer without its lease. LdbContext reads see only the snapshot it opened at; inside the writer use Rows&lt;T&gt; on the batch's transaction. LDB 0.1.0 has one [Key] per entity: multi-part keys are concatenated (RowKeys) and enum columns compare as their underlying number. A local admit is StoreFull when pinned bytes plus the object exceed the budget, pinned or not. Do not reference Axiom.LWSE.Core before 0.3.0 is on nuget.org, nor Protocol, Local or Discovery types until their milestones land.
</pitfalls>
