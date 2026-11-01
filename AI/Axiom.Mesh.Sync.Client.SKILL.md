<identity>
    <package>Axiom.Mesh.Sync.Client</package>
    <role>addon-skill</role>
    <requires>Axiom.Mesh.Local.SKILL.md</requires>
</identity>

<objective>
    Teach an agent to write an app (or app commands) over the sync service's namespace axiom-mesh-sync/1: call its methods with SyncCall over ServiceCall, read its events with SyncEventReader, and explain its statuses, without ever referencing the service's executable.
</objective>

<package_scope>
    Namespace Axiom.Mesh.Sync. SyncProtocol { ServiceName "sync", Namespace "axiom-mesh-sync/1", MarkerName ".axiom-mesh-sync", DefaultLabel "main" }. SyncMethods (bindings, bind, unbind, status, scan, conflicts) and SyncEvents (binding, conflict): const string tables. One record per request, reply and event: BindingsRequest/BindingsReply { Bindings <=64 }, BindRequest { Directory absolute, PackageId, Label, Symlinks (SymlinkPolicy AsFile|Ignore), Ignore <=64 patterns: names and *.ext }/BindReply { Binding }, UnbindRequest { Directory }/UnbindReply { Binding last seen }, StatusRequest { Directory: a path inside a binding, empty for all }/StatusReply { Bindings }, ScanRequest { Directory, empty for all }/ScanReply { Queued }, ConflictsRequest { Directory, After, Limit 1-1024 }/ConflictsReply { Conflicts, More }. Binding { Directory, PackageId, Label, Symlinks, Ignore, State (BindingState InSync|Scanning|Pushing|Pulling|LocalChangesReadOnly|WaitingForObjects|Error), Rights (GrantRights), Base, Head, Leaves, Files, Bytes, LocalChanges, Conflicts, LastRun ms, Runs, Pending, Error <=256 bytes }. Conflict { Id, Directory, Path, Kind (ConflictKind CopyWritten|NameRefused|TooLarge|Unreadable|TooManyEntries), CopyPath, OtherDevice, Version, At ms }. Events derive from SyncEvent (closed set, Name): BindingEvent { Binding }, ConflictEvent { Conflict }. ISyncMethod<TSelf, TReply> (static Method) pairs a request with its reply. SyncCall.SendAsync(LocalClient user, request, ct) -> LocalResult<TReply>. SyncEventReader.TryRead(ServiceEvent, out SyncEvent) and Filter for topic service. SyncStatus { None 0, AlreadyBound 32768, NotADirectory, NestedBinding, NotFollowed, NotAnArchive, NotBound, UnknownMethod 32774 }, SyncStatusText.IsSyncStatus/Explain. SyncLimits (Bindings 64, IgnorePatterns 64, PatternLength 255, PathLength 4096, EntriesPerDirectory 8192, ConflictsPage 1024, LabelLength 64, ErrorLength 256, PayloadBytes 1 MiB, DebounceQuiet 2 s, DebounceCap 30 s, Stability 2 s, Rescan 10 min). SyncCodec.Encode/TryDecode/SizeOf for any record.
</package_scope>

<architecture>
    Every method is one ServiceCall to service sync and answers at once from the service's state: the request's payload, the reply's payload, or a status (LOCAL-SPEC.md §11.1, or a SyncStatus from 32768) with the service's Error. Status codes from 32768 are per service: the chat service gives the same numbers other meanings, so explain a sync call's status with SyncStatusText only. Work a method starts (bind, scan) runs afterwards; binding events report every state at most once a second while a run goes and always at its end; Binding.Pending says a run is queued or running. Events carry the zero Package. SYNC-SERVICE.md is the specification; Documentation/vectors/sync/records.txt holds a payload of every record.
</architecture>

<best_practices>
    Follow or create the Archive as the user first (FollowPackage, CreatePackage Archive), then bind. Wait for a binding's run with status until Pending is false and Runs grew, or with binding events subscribed before acting, never for time. Find the binding of the current directory by walking up to the marker file .axiom-mesh-sync, then ask status for that directory. Page conflicts with After = the last Id while More. Show a read-only binding (Rights below Write) as such; its LocalChanges are kept, not published.
</best_practices>

<pitfalls>
    Directories are the service's: absolute, read and written as the Host's user. A directory inside or above a bound one is NestedBinding. There is no resolve method: a conflict is resolved by deleting one copy in the folder. Unbinding leaves every file; binding the folder again merges what it holds with the branch as local changes. SyncEvent is a closed set; an unknown event name (a later namespace version) reads as false, ignore it.
</pitfalls>
