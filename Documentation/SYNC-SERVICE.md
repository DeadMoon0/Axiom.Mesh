# The sync service, `axiom-mesh-sync/1`

**Status:** Draft 1, 2026-11-01 (milestone S2). The namespace the sync service offers every app over the local protocol
([LOCAL-SPEC.md](LOCAL-SPEC.md) draft 5), and what the service does behind it: how a folder is bound to an Archive and kept in
step with one of its branches both ways, with the branches and merges of [SPEC.md](SPEC.md) §9.5. Normative for the service
(`Services/Axiom.Mesh.Sync`, the executable `axiom-mesh-sync`) and for every app that speaks the namespace, through the
`Axiom.Mesh.Sync.Client` library or its own encoder. The payload the service reads and writes is the `Version` of
[PAYLOADS.md](PAYLOADS.md) §2.

## Table of contents

1. Introduction
2. Bindings
3. The run
4. Records
5. Methods
6. Events
7. Statuses
8. Branches and merges as applied
9. Limits
10. Storage
11. Conformance and scenarios
12. Test vectors
13. Security
14. Layouts at a glance
15. Worked example

## 1. Introduction

The sync service defines the Package kind `Archive` (1) with the Entry kind `Version` (1) (SPEC.md §14.7, §14.8). It binds a
directory to an Archive and one of its branches, publishes the directory's changes as Versions and writes the branch's
changes into the directory, merging concurrent Versions by the rules of SPEC.md §9.5 so that every device that holds the same
Versions ends with the same files. **A service owns no user interface** (DESIGN.md §10.3, S-D13): the command line's
`sync …` commands and the app's sync view are two apps over the records of this namespace.

Out of scope in draft 1, each for a later phase: rename detection (a moved file is a deletion and an addition; its bytes are
not sent again, they are content-addressed), local symlinks (never read nor published), a resolve method (the user deletes
the copy not wanted), per-kind retention (STRUCTURAL-DEBT.md 54), syncing a part of a branch.

## 2. Bindings

A **binding** is one directory, one Archive, one branch (`label`, `main` by default). The service keeps at most 64. A
directory already bound, one inside a bound directory and one holding a bound directory are refused (`AlreadyBound`,
`NestedBinding`): a file belongs to one binding. Paths are compared with `/` and `\` alike and, on Windows, without case.

- The service follows no Archive and opens no user connection: an app creates or follows the Archive as the user, then calls
  `bind`. `bind` replies once the binding is recorded and its marker written, and queues the first run.
- The **marker file** `.axiom-mesh-sync` in the directory holds the namespace, the binding's id (16 bytes: the start of
  SHA-256 over `axiom-mesh-sync/binding/1`, the directory and the Archive) and the Archive. An app finds the binding a path
  lies in by walking up to it. The state lives in the service's database only.
- Names starting with `.axiom-mesh-` are never published nor written (the marker, the helpers' temporary files). Ignore
  patterns (at most 64) are exact names and `*.ext`, matched case-insensitively against every name of a path; an ignored
  directory is left alone with all in it. An ignored path keeps the state the branch had: leaving a path alone never reads
  as deleting it.
- **Rights** below `Write` make a read-only binding: it pulls and never writes an Entry; local changes are kept, counted in
  `localChanges`, and the state is `LocalChangesReadOnly`.
- **Modes.** A file is mode 1, an executable file mode 2 where the file system states the bit (Linux); on Windows a file the
  branch made executable stays executable (its fingerprint remembers it). A symlink of the branch is written as a regular
  file holding its target text, whose fingerprint remembers it was a symlink so a later scan publishes it unchanged; with
  `symlinks = Ignore` it is not written and keeps the branch's state. Local symlinks are never followed nor published.
- `unbind` cancels the run, removes the watcher, the rows and the marker; every file stays. Binding the directory again is a
  first sync: what it holds is merged with the branch as local changes.

## 3. The run

One run at a time per binding, at most four over all bindings. Triggers are coalesced: the directory's watcher after its
debounce (a run starts once the directory was quiet for 2 s, at most 30 s after the burst's first change; a watcher that
lost events asks for a run at once), a rescan every 10 minutes, the service's start, a new `Version` of the binding's Archive
in the runtime's stream, a change of the Package, `bind` and `scan`.

1. **Resume.** A binding whose `pendingTree` is set resumes it: steps 2 to 5, then 10 to 12 for that Tree, without planning.
2. **Scan.** The directory's entries without reading a byte: size, write time, executable bit; links skipped; a name a Tree
   cannot hold is `NameRefused`, a directory of more than 8 192 entries `TooManyEntries`, one that cannot be listed
   `Unreadable`, each a conflict whose paths keep the branch's state.
3. **Detect.** A file whose size, write time and mode match its fingerprint is not read. A fingerprint taken less than 2 s
   after its file's last write is racy (a same-size write within the same tick of the file system's clock would not show)
   and the file is read again. A file changed within the last 2 s is busy: read at a later run, its old fingerprint stands
   meanwhile, and a run comes again once it settled.
4. **Read** what changed with `StoreFiles.PutFileAsync` (a few files at once), each object recorded as a put row; the
   fingerprint takes the size and write time the scan saw. A file larger than 32 GiB is `TooLarge`.
5. **Build** the directory's Trees from the deepest directory up (explicit empty directories kept).
6. **Branch.** The Archive's Versions, the branch's leaves and the highest clock; no run plans while the Version DAG lacks a
   Version the Host stored (it comes again when the stream delivers it).
7. **Plan** (`SyncPlanner`): Nothing, Publish, Pull, MergeChain, PublishThenMerge, FirstSync or PullReadOnly (§8).
8. **Publish**: the Trees put; a `Version { tree, label, "", "" }` written with its parents named explicitly and ascending,
   `references = [tree]`; then the base (`baseTree`, `baseLeaves`) committed. Never a Version whose Tree equals the Tree of a
   leaf this device already wrote: that leaf stands for it. "This device" is the Host's identity as the run starts
   (`ServiceSession.Identity`, kept current by the runtime, LOCAL-SPEC.md §9.1), never the `HelloAck`'s; a run on a Host
   that has no identity yet ends `Error` before it reads a byte, so no Version ever names the zero device.
9. **Merge** the two leaves lowest in the total order, again while more remain (§8); the Trees of every leaf are fetched
   first (`Fetch`, recursive; the binding waits as `WaitingForObjects` when no peer supplies them, and tries again after 2 s,
   doubling to at most a minute). A merge Version is written before the directory changes.
10. **Record intent**: `pendingTree` and `pendingLeaves` committed before anything on disk changes.
11. **Write** the disk plan (`DiskDiff`): deletes the deepest first (a directory only once empty), directories, then each file
    with `StoreFiles.WriteFileAsync` (every Chunk held, the size checked, a hidden temporary name renamed into place) and its
    fingerprint after it. A path the directory already holds as the target has it is not written again; a path changed in the
    directory since the base is not touched (counted in `localChanges`); a name this file system cannot hold or a
    case-insensitive collision is `NameRefused`.
12. **Commit** the base: `baseTree`, `baseLeaves`, `pendingTree` cleared, the conflicts recorded.
13. A merge Version was written at step 9; a run with leaves left over is triggered again by the stream.
14. **Release** every put row's pin with `Unpin`, those of runs that stopped before their end included; the `binding` event.

**Invariants**, each proved by stopping a run where a crash would (`Sync.Tests: CrashResumeTests`):

1. Everything put is content-addressed: doing a step again changes nothing.
2. A leaf of this device that names a Tree stands for a Version a crash left unrecorded: no second Version.
3. The target is pending before the directory changes and the base moves after it: a resumed run writes only what is left.
4. A file changed in the directory since the base is never overwritten or deleted by a pull; nothing is ever partial under
   its name.
5. A read-only binding never writes an Entry.

## 4. Records

Every payload is exactly one record in the types of SPEC.md §5.2, canonical as SPEC.md §5.3, read to its end; a payload with
bytes after its record, or a field out of bounds, is no record. `time` is a `u64` of milliseconds since the Unix epoch.

| Record | Fields |
|---|---|
| `Binding` | `directory string(4096)`, `packageId id`, `label string(64)`, `symlinks u8`, `ignore list<string(255)>(64)`, `state u8`, `rights u8`, `base hash`, `head hash`, `leaves u16`, `files u64`, `bytes u64`, `localChanges u32`, `conflicts u32`, `lastRun time`, `runs u64`, `pending bool`, `error string(256)` |
| `Conflict` | `id u64`, `directory string(4096)`, `path string(4096)`, `kind u8`, `copyPath string(4096)`, `otherDevice id`, `version hash`, `at time` |

`state`: 1 InSync, 2 Scanning, 3 Pushing, 4 Pulling, 5 LocalChangesReadOnly, 6 WaitingForObjects, 7 Error. `symlinks`: 1
AsFile, 2 Ignore. `kind`: 1 CopyWritten, 2 NameRefused, 3 TooLarge, 4 Unreadable, 5 TooManyEntries. `base` is the Tree the
directory last equalled; `head` the branch's head for display (SPEC.md §9.5); `leaves` how many leaves this Host holds;
`pending` whether a run is queued or running; `error` why the binding is in `Error` or `WaitingForObjects`.

## 5. Methods

| Method | Request | Reply | Section |
|---|---|---|---|
| `bindings` | `BindingsRequest` | `BindingsReply` | 5.1 |
| `bind` | `BindRequest` | `BindReply` | 5.2 |
| `unbind` | `UnbindRequest` | `UnbindReply` | 5.3 |
| `status` | `StatusRequest` | `StatusReply` | 5.4 |
| `scan` | `ScanRequest` | `ScanReply` | 5.5 |
| `conflicts` | `ConflictsRequest` | `ConflictsReply` | 5.6 |

Every method replies at once from the service's state; work it starts runs afterwards and `binding` events report it. A
request the service cannot read is `Invalid` with detail `payload`; a method it does not know is `UnknownMethod`.

### 5.1 `bindings`

Empty request. Reply `bindings list<Binding>(64)`, by directory.

### 5.2 `bind`

Request `directory string(4096)` (absolute, existing), `packageId id`, `label string(64)` (not empty), `symlinks u8`,
`ignore list<string(255)>(64)`. Reply `binding Binding`. Refusals: `NotADirectory`, `AlreadyBound`, `NestedBinding`,
`NotFollowed`, `NotAnArchive`, `Busy` (64 bindings), `Invalid` (detail `label`, `symlinks` or `ignore`), `IoError` (the marker
cannot be written).

### 5.3 `unbind`

Request `directory string(4096)`: the bound directory. Reply `binding Binding` as it was last. `NotBound` for a directory no
binding holds. The files stay.

### 5.4 `status`

Request `directory string(4096)`: a path inside a bound directory, or empty. Reply `bindings list<Binding>(64)`: the one
binding the path lies in, or every binding. `NotBound` for a path no binding holds.

### 5.5 `scan`

Request `directory string(4096)`: a path inside a bound directory, or empty for every binding. Reply `queued u16`. `NotBound`
for a path no binding holds.

### 5.6 `conflicts`

Request `directory string(4096)` (a path inside a bound directory, or empty for all), `after u64`, `limit u16` (1 to 1 024).
Reply `conflicts list<Conflict>(1024)` ascending by id after `after`, `more bool`; a page is also cut to fit 1 MiB. `NotBound`
for a path no binding holds; a `limit` out of bounds makes the payload no record (`Invalid`).

## 6. Events

| Event | Record | Package | Section |
|---|---|---|---|
| `binding` | `BindingEvent` | zero | 6.1 |
| `conflict` | `ConflictEvent` | zero | 6.2 |

An app subscribes to topic `service` with `Filter.Service = "sync"`.

### 6.1 `binding`

`binding Binding`: a state a run passed through (at most one a second) and always its end.

### 6.2 `conflict`

`conflict Conflict`: one the service recorded at the end of a run.

## 7. Statuses

| Code | Name | Meaning |
|---|---|---|
| 32768 | `AlreadyBound` | the directory is bound already |
| 32769 | `NotADirectory` | the path is not an existing directory given absolute |
| 32770 | `NestedBinding` | the directory lies inside a bound directory, or holds one |
| 32771 | `NotFollowed` | the Package is not followed on this Host |
| 32772 | `NotAnArchive` | the Package is not an Archive |
| 32773 | `NotBound` | no binding holds the directory |
| 32774 | `UnknownMethod` | the service knows no such method |

Codes from 32 768 are the service's own (LOCAL-SPEC.md §11.2): the chat service gives the same numbers other meanings, so an
app explains a sync call's status with the sync table. Every other status is one of LOCAL-SPEC.md §11.1 with its meaning
there.

## 8. Branches and merges as applied

**Leaves** (SPEC.md §9.5): the stored Versions with the binding's label from which no other stored Version with that label
descends, through any parents of any label; the **head** for display is the leaf highest in the total order. The owner's
Manifest `branches` list is shown as the owner's declaration only; it never overrides the order.

**Plans.**

- The branch has no Version: the directory is published if it holds anything, naming the Archive's highest Version of any
  branch as parent when there is one (a fork, SPEC.md §9.3), none when the Archive is empty.
- No base, or a base whose leaves are no longer stored (a new binding, another store epoch): a **first sync**. An empty
  directory pulls the branch. A non-empty one is merged against the empty tree with the branch reduced to one leaf, the
  directory standing as the Version this device would write next (clock one above every clock the Archive holds, so later
  than every leaf): its files win a conflict and the leaf's file lies beside. That merge is written naming the leaf as parent,
  never as a Version of the directory alone over the leaf; when it equals the leaf's Tree nothing is written.
- The leaves are the base's: the directory's changes are published naming them, or nothing happens.
- The leaves moved and the directory did not change: pull the one new leaf, or merge the leaves.
- Both moved: the directory is published naming the base's leaves, then the leaves are merged.
- A read-only binding writes no Version: it merges the leaves in memory and writes the result into the directory, keeping
  every local change.

**Merging** (SPEC.md §9.5): the two leaves lowest in the total order are merged into a Version whose parents are both, again
while more than one leaf remains, so a chain of two-parent merges resolves any number of concurrent leaves. The common
ancestor is the Version highest in the total order among those reachable from both leaves, each reachable from itself; with
several unrelated common ancestors (a criss-cross) the highest one is taken and nothing is merged recursively; with none,
the empty tree. Path by path over the union of both Trees against the ancestor's:

| Changed on | Result |
|---|---|
| neither side | the ancestor's entry |
| one side only, a deletion included | that side's state |
| both sides, identically | that entry |
| both sides, differently | the later leaf's entry, and the other side's file or subtree beside it |

A directory changed on both sides is merged path by path within it (a side without it counts as an empty directory; a
directory the merge leaves empty that one side deleted is gone). A directory replaced by a file, or the reverse, is changed
differently: the later side's entry stands and the other side's file **or whole subtree** lies beside it whatever its mode.
This is a stricter reading of SPEC.md §9.5's "the other side's file", recorded in CONFORMANCE.md (a proposed SPEC draft-6
note, not applied to SPEC.md): nothing of either side is lost. No content is merged.

**Conflict names**: `<name>.conflict-<first 8 hex of the other deviceId>` before the extension, which starts at the first dot
that is not the name's first character (`report.conflict-ab12cd34.txt`, `archive.conflict-ab12cd34.tar.gz`,
`Makefile.conflict-ab12cd34`, `.bashrc.conflict-ab12cd34`); a taken name gets `-2`, `-3`, … appended to the tag
(`report.conflict-ab12cd34-2.txt`); names compare case-insensitively on every platform, so every device chooses the same; the
result is at most 255 UTF-8 bytes, the stem shortened first.

**Elision**: two leaves whose merge equals the later leaf's Tree write no merge Version; both stay the base's leaves, and the
device's next publish names both as parents. The merge does not depend on which leaf a device calls its own: every device
that merges the same two leaves makes the same Tree; when two devices both write it, the next merge of their two Versions
equals both and is elided.

## 9. Limits

| Bound | Value |
|---|---|
| Bindings per service | 64 |
| Ignore patterns per binding | 64, each at most 255 bytes |
| Path | 4 096 bytes |
| Entries per directory published | 8 192 (a Tree, SPEC.md §8.4); a larger directory is a conflict |
| Conflicts per page | 1 024, cut to 1 MiB |
| Debounce | 2 s quiet, 30 s at most after the first change |
| Stability, racy window | 2 s each |
| Rescan | every 10 minutes |
| Runs at once | 4 over all bindings |
| Retry while waiting for objects | 2 s, doubling to 1 minute |

## 10. Storage

The service's own LDB database (S-D2) under its state directory, `<home>/services/sync/index`, shared with the runtime's
cursor. Schema 1:

| Table | Key | Holds |
|---|---|---|
| `SyncMeta` | `meta` | schema version, store epoch, store sequence of the last Version the stream delivered, next conflict number |
| `SyncBindings` | binding id (16 bytes) | directory, Archive, label, symlinks, ignore patterns, `baseTree`, `baseLeaves`, `pendingTree`, `pendingLeaves`, state, error, last run, runs, files, bytes, local changes |
| `SyncFingerprints` | binding id ‖ SHA-256 of the path | binding (indexed), path, mode, size, write ticks, taken ticks, hash |
| `SyncVersions` | Entry hash | Archive (indexed), clock, device, parents, label, Tree, store sequence |
| `SyncConflicts` | number (8 bytes) | number (indexed, the paging order), binding (indexed), path, kind, copy path, other device, Version, time |
| `SyncPuts` | binding id ‖ object hash | binding (indexed), object |

Bindings and conflicts commit durably; Versions, fingerprints and puts lazily (a crash loses a suffix, in order, and the next
run or the stream brings it again). **Another store epoch** (LOCAL-SPEC.md §9.3) drops `SyncVersions` and every binding's
leaves and pending Tree, and keeps base Trees and fingerprints, which describe the disk: the next run of every binding is a
first sync, and no local edit is lost.

## 11. Conformance and scenarios

The rules are pure functions in `Services/Axiom.Mesh.Sync/Rules` (a convention test proves the folder touches no disk, clock
or Host): `Leaves`, `Ancestor`, `TreeFlattener`, `TreeSet`, `Merge`, `ConflictName`, `ChangeDetection`, `ModeMemory`,
`IgnoreRule`, `VirtualLeaf`, `BindingRule`, `SyncPlanner`, `DiskDiff`, `LocalTree`, `Debounce`, each tested from this
document and SPEC.md §9.5 (`Sync.Tests: Rules/`). The scenario suite `SyncScenarios` runs unchanged in memory and on real
sockets with the real pipe: a folder appearing byte-equal on the other side; an edit reaching back with one leaf on both;
edits to different files while apart merging into one merge Version with two parents; the same file edited on both, the
later leaf winning and the other side's copy beside on both; deletion and directory↔file both ways; many small files and a
large one; a service stopped mid-sync resuming without a duplicate or a partial file; a stopped service catching up; a
read-only follower; unbind and rebind; an offline node catching up without writing a Version; three concurrent leaves
resolved by a chain of merges; another store epoch as a first sync; a Version of another branch ignored. `Host.Tests:
SyncServiceTests` spawns the real service from a real daemon. Rows in [CONFORMANCE.md](CONFORMANCE.md) under
"Axiom.Mesh.Sync".

## 12. Test vectors

`Documentation/vectors/sync/records.txt` holds the payload of one sample of every record of §4 to §6 as hex
(`<record> <hex>`). An implementation decodes each and re-encodes it to the same bytes (`Sync.Client.Tests: VectorTests`).

## 13. Security

The service runs as the Host's user and reads and writes only the bound directories, as that user; an app that may open the
pipe may already act as that user (LOCAL-SPEC.md §2.2). It holds no key: the Host signs every Entry. It never follows a link
of the directory, never writes a name the file system would read as another path (SPEC.md §8.4), never writes outside the
directory, and never writes a file the user changed since its last run. A Version a peer wrote is merged and written like any
other; what it holds is the Archive's writers' to answer for (SPEC.md §9.6).

## 14. Layouts at a glance

```
Binding:          directory string(4096), packageId id, label string(64), symlinks u8, ignore list<string(255)>(64), state u8,
                  rights u8, base hash, head hash, leaves u16, files u64, bytes u64, localChanges u32, conflicts u32,
                  lastRun time, runs u64, pending bool, error string(256)
Conflict:         id u64, directory string(4096), path string(4096), kind u8, copyPath string(4096), otherDevice id,
                  version hash, at time
BindingsRequest:  (empty)                                        BindingsReply:  bindings list<Binding>(64)
BindRequest:      directory string(4096), packageId id, label string(64), symlinks u8, ignore list<string(255)>(64)
BindReply:        binding Binding                                UnbindReply:    binding Binding
UnbindRequest, StatusRequest, ScanRequest:  directory string(4096)
StatusReply:      bindings list<Binding>(64)                     ScanReply:      queued u16
ConflictsRequest: directory string(4096), after u64, limit u16   ConflictsReply: conflicts list<Conflict>(1024), more bool
BindingEvent:     binding Binding                                ConflictEvent:  conflict Conflict
```

## 15. Worked example

Alice keeps `D:\shared` in sync with a new Archive; Bob joins with an empty folder; both edit `report.txt` while Bob is offline.

```
alice> axiom-mesh sync add D:\shared --create Shared
  # CreatePackage(Archive, Shared) as the user, then ServiceCall(sync, bind, BindRequest{ D:\shared, <Shared>, main })
  # the first run: no Version yet -> Publish(parents []): Version{ tree T1, main } written, base T1
bob$ axiom-mesh sync add ~/shared --package Shared
  # FollowPackage, bind; the first run: an empty folder -> Pull(V1): T1 written, base T1

  ... Bob goes offline; both change report.txt; each run publishes: Alice V2a (parents [V1]), Bob V2b (parents [V1]) ...

  # Bob comes back; Alice's run sees leaves {V2a, V2b}, base leaves {V2a}, the folder unchanged -> MergeChain:
  #   ancestor V1; report.txt changed differently; V2b is later in the total order (same clock, Bob's device id higher)
  #   merged: report.txt = Bob's, report.conflict-<8 hex of Alice's device>.txt = Alice's
  #   Version M { merged } with parents [V2a, V2b] written, then the folder written, base M
  # Bob's run sees leaves {M} -> Pull(M): Alice's copy appears beside his file
bob$ axiom-mesh sync conflicts
ID  AT                    KIND          PATH        COPY                               OTHER DEVICE
1   2026-11-01 10:00:02Z  copy written  report.txt  report.conflict-1a1b1c1d.txt       1a1b1c1d1e1f
```

(The conflict row is recorded by the device that made the merge; the other device holds the same files.)
