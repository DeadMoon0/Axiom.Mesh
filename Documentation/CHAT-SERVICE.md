# The chat service, `axiom-mesh-chat/1`

**Status:** Draft 1, 2026-11-01 (milestone S1). The namespace the chat service offers every app over the local
protocol ([LOCAL-SPEC.md](LOCAL-SPEC.md) draft 5), and what the service does behind it. Normative for the service
(`Services/Axiom.Mesh.Chat`, the executable `axiom-mesh-chat`) and for every app that speaks the namespace, through the
`Axiom.Mesh.Chat.Client` library or its own encoder. The payloads the service reads and writes are those of
[PAYLOADS.md](PAYLOADS.md) §3 and §4.

## Table of contents

1. Introduction
2. Transport and namespace
3. Encoding
4. Records
5. Methods
6. Events
7. Statuses
8. Jobs
9. Limits
10. Index
11. Rules
12. Conformance and scenarios
13. Test vectors
14. Security
15. Layouts at a glance
16. Worked example

## 1. Introduction

The chat service defines the Package kind `Channel` (2) with the Entry kinds `Message` (2) and `Redaction` (3) (SPEC.md
§14.7, §14.8). It keeps an index of every Channel the Host follows in a database of its own, and offers it to apps: a
Channel's timeline in the total order of SPEC.md §9.3, threads, attachments in and out, writing and withdrawing messages.
**A service owns no user interface** (DESIGN.md §10.3, S-D13): what an app shows of a Channel comes from this namespace
alone, so the command line's `chat …` commands and the app's chat view are two apps over the same records.

Out of scope in draft 1, each for a later phase:

- **Read markers.** Which message a device last showed is the app's state; when it is shared between a user's devices
  it will be an Entry kind of its own in a per-user Package the service does not own (phase 3), never a field of a
  message.
- Edits, reactions, typing indicators and presence; search over texts; notifications outside the `message` event.
- Per-kind retention (STRUCTURAL-DEBT.md 54): a desktop keeps every Channel Entry its policy keeps.

## 2. Transport and namespace

The service connects in the service role as `chat` (LOCAL-SPEC.md §4.2, §9.1); on a desktop the Host spawns it as
`axiom-mesh-chat` (S-D1) with the token, the pipe and its state directory in its environment. An app reaches it with
`ServiceCall` (LOCAL-SPEC.md §6.7) naming service `chat`, a method of §5 and the method's request as the payload; the
reply's payload is the method's reply record, or the call fails with a status of LOCAL-SPEC.md §11.1 or of §7 and the
service's `Error`. The service pushes the events of §6 with `Emit` (LOCAL-SPEC.md §9.10); an app receives them on topic
`service` with `Filter.Service = "chat"`, and a Package id to watch one Channel. The namespace's name is
`axiom-mesh-chat/1`; `about` answers it, and a later incompatible version takes another name.

`ServiceCall` is unordered against the caller's other requests (LOCAL-SPEC.md §3.4): an app that needs one call's effect
before the next waits for the first reply. `send` and `withdraw` are not idempotent; every other method is.

## 3. Encoding

Every payload is exactly one record of §4 in the types of SPEC.md §5.2, canonical as SPEC.md §5.3, read to its end; a
payload with bytes after its record, or a field out of bounds, is no record. Besides the types of SPEC.md §5.2 the
records use `EntryKey` (`clock u64, deviceId id, hash hash`, LOCAL-SPEC.md §5.6), `optional<T>` (a `bool`, then the
value when true) and `Attachment` (`name string(255), hash hash, size u64`, PAYLOADS.md §3). A request the service
cannot read is `Invalid` with `Error.detail` `payload`; a method it does not know is `Invalid` with detail `method`.

## 4. Records

| Record | Fields |
|---|---|
| `AuthorView` | `deviceId id`, `userId id` (zero when no Roster held lists the device), `userName string(64)`, `deviceName string(64)` |
| `ChannelView` | `packageId id`, `name string(255)`, `ownerId id`, `ownerName string(64)`, `followed bool`, `messages u64`, `withdrawn u64`, `last optional<EntryKey>`, `indexed bool` |
| `MessageView` | `key EntryKey`, `packageId id`, `author AuthorView`, `stored time` (when this Host stored it), `text string(16384)`, `attachments list<Attachment>(64)`, `replyTo hash`, `withdrawn bool`, `withdrawnBy id`, `broken bool` |
| `JobView` | `job u32`, `kind u8` (§8), `state u8` (§8), `done u64`, `total u64`, `message hash`, `status u16`, `attachment optional<Attachment>`, `path string(4096)`, `problem string(256)` |

A withdrawn message (an honoured Redaction names it, SPEC.md §9.4) and a broken one (§11) carry an empty `text` and no
`attachments`, whatever the Entry holds: the service never hands out what was withdrawn. `stored` is this Host's; two
devices agree on everything else of a message.

## 5. Methods

| Method | Request | Reply | Section |
|---|---|---|---|
| `about` | `AboutRequest` | `AboutReply` | 5.1 |
| `channels` | `ChannelsRequest` | `ChannelsReply` | 5.2 |
| `timeline` | `TimelineRequest` | `TimelineReply` | 5.3 |
| `message` | `MessageRequest` | `MessageReply` | 5.4 |
| `thread` | `ThreadRequest` | `ThreadReply` | 5.5 |
| `send` | `SendRequest` | `SendReply` | 5.6 |
| `withdraw` | `WithdrawRequest` | `WithdrawReply` | 5.7 |
| `attach` | `AttachRequest` | `AttachReply` | 5.8 |
| `get` | `GetRequest` | `GetReply` | 5.9 |
| `jobs` | `JobsRequest` | `JobsReply` | 5.10 |

### 5.1 `about`

Empty request. Reply `namespace string(64)`, `version string(64)`, `storeEpoch id` (the epoch the index was built under),
`indexedSeq u64` (the store sequence of the last Entry it took in from the Host's stream), `caughtUp bool` (the Host holds
no Entry of the chat kinds after `indexedSeq`: a timeline read now is complete), `channels u32`. An app that must see
everything stored waits for `caughtUp`, never for time.

### 5.2 `channels`

Empty request. Reply `channels list<ChannelView>(256)`, by name then Package id.

### 5.3 `timeline`

Request `packageId id`, `after optional<EntryKey>`, `reverse bool`, `limit u16` (1 to 256). Reply `messages
list<MessageView>(256)`, `more bool`: the Channel's messages after `after` (before it when `reverse`) in the total order,
cut at `limit`, 256 or 1 MiB of reply, whichever comes first; continue after the last message when `more`. A Channel the
service does not know is `NotAChannel`; a limit outside 1 to 256 `Invalid`.

### 5.4 `message`

Request `hash hash`. Reply `message MessageView`. Not indexed: `NotAMessage`.

### 5.5 `thread`

Request `message hash` (any message of the thread), `limit u16` (1 to 256). Reply `root MessageView`, `replies
list<MessageView>(256)`, `more bool`. The root is found up `replyTo` through indexed messages, at most 64 steps; the
replies are every message whose chain leads to the root, at any depth, in the total order, cut like a timeline. A
withdrawn root keeps its replies.

### 5.6 `send`

Request `packageId id`, `text string(16384)`, `attachments list<Attachment>(64)`, `replyTo hash`. Reply `key EntryKey`.
The service writes a Message (PAYLOADS.md §3) with `WriteEntry`, its envelope's `references` the attachments' hashes,
then releases its pins on the objects its `attach` jobs put for them (LOCAL-SPEC.md §9.5), indexes the Entry and emits
`message`. A Channel not followed here is `NotAChannel`; neither text nor attachment, or an attachment without a name,
`Invalid`; a `replyTo` not indexed in the same Channel `NotAMessage`; an attachment not stored (`WriteEntry` answers
`NotFound`) `AttachmentMissing`; the Host's refusals (`NotWritable`, `ClockExhausted`, …) pass through.

### 5.7 `withdraw`

Request `message hash`. Reply `key EntryKey` (the Redaction's). The service writes a Redaction (PAYLOADS.md §4) whose
`redacts` and `target` name the message. Only the device that wrote the message or a device of the Channel's owner may
(SPEC.md §9.4); the Host would store any other Redaction without honouring it, so the service checks first: `NotYours`.
Not indexed: `NotAMessage`; withdrawn already: `Withdrawn`. The own device and user are the Host's identity as it stands
(`ServiceSession.Identity`, which the runtime keeps current over topic `users`, LOCAL-SPEC.md §9.1), not the `HelloAck`'s:
a service spawned before `identity create` withdraws like one started after it. A Host without an identity has no device
that wrote or owns anything: `NotYours`.

### 5.8 `attach`

Request `path string(4096)` (absolute), `name string(255)` (empty for the path's last name). Reply `job u32`. A job (§8)
stores the file as a ChunkList (`StoreFiles.PutFileAsync`) or the folder as a Tree (`Trees.PublishDirectoryAsync`) as
the service's own user; its `attachment` names the ChunkList or root Tree and the size, a folder's being the sum of its
files' sizes (S-D10, PAYLOADS.md §3), for display only. A relative path is `Invalid`, nothing there `NotFound`, 16 jobs
running `Busy`.

### 5.9 `get`

Request `hash hash` (the attachment), `message hash`, `path string(4096)` (absolute), `existing u8` (LOCAL-SPEC.md §8.6:
1 fail, 2 replace, 3 merge). Reply `job u32`. A job fetches the attachment recursively from the peers (`Fetch`, its
operation followed on topic `operations`) and writes it: a ChunkList as a file (`StoreFiles.MaterialiseFileAsync`, fail
or replace), a Tree as a folder (`StoreFiles.MaterialiseAsync`, merge too), never a partial file under the name. Not
indexed: `NotAMessage`; withdrawn: `Withdrawn`; no such attachment of the message: `AttachmentMissing`.

### 5.10 `jobs`

Empty request. Reply `jobs list<JobView>(64)`: every running job and the ended ones remembered, by number.

## 6. Events

| Event | Record | Package | Section |
|---|---|---|---|
| `message` | `MessageEvent` | the Channel | 6.1 |
| `withdrawn` | `WithdrawnEvent` | the Channel | 6.2 |
| `channel` | `ChannelEvent` | the Channel, zero once unfollowed or dropped | 6.3 |
| `job` | `JobEvent` | zero | 6.4 |

### 6.1 `message`

`key EntryKey`, `packageId id`, `author AuthorView`, `preview string(256)` (the text's start, cut between characters),
`attachments u16`, `replyTo hash`. Emitted for a message written by `send`, or taken in while connected (its store
sequence above the Host's at connect); never for a withdrawn or broken one, nor for the messages a catch-up takes in.

### 6.2 `withdrawn`

`key EntryKey` (the message's), `packageId id`, `by id` (the device whose Redaction withdrew it).

### 6.3 `channel`

`change u8` (1 indexed, 2 followed, 3 unfollowed, 4 dropped, 5 renamed), `channel ChannelView`. A Channel no longer
followed is no Package the Host lets a service emit for (LOCAL-SPEC.md §9.10), so its `unfollowed` and `dropped` events
carry the zero Package and reach only subscriptions that take every Package.

### 6.4 `job`

`job JobView`: a start and an end always, progress at most once a second on the service's clock. With the zero Package:
an app that waits for a job subscribes with a zero Package filter before it starts the job.

## 7. Statuses

| Code | Name | Meaning |
|---|---|---|
| 32768 | `NotAChannel` | the Package is no Channel the service knows (not followed here, or another kind) |
| 32769 | `NotAMessage` | the hash names no indexed message |
| 32770 | `NotYours` | only the message's device or a device of the Channel's owner may withdraw it |
| 32771 | `AttachmentMissing` | the attachment is not stored here, or the message names no such attachment |
| 32772 | `Withdrawn` | the message is withdrawn; nothing of it is shown or fetched |
| 32773 | `JobNotFound` | no such job (reserved for a method that names a job) |

Every other status a call answers is one of LOCAL-SPEC.md §11.1 with its meaning there: `Invalid` (detail names the
field), `NotFound`, `Busy`, `IoError`, `ServiceUnavailable` and `ServiceTimeout` from the Host, and the Host's own
refusals of `WriteEntry` passed through.

## 8. Jobs

Long work is a job (S-D8): the call that starts it answers at once with the job's number, and `job` events report it.
Jobs are numbered from 1 while the service runs; at most 16 run at once (one more is `Busy`); the last 48 ended ones are
remembered for `jobs`. `kind`: 1 attach, 2 get. `state`: 1 running, 2 done, 3 failed (then `status` and `problem` say
why). A job works on the connection that started it and fails when that connection ends.

The objects an `attach` put keep the service's pins (reason `Service`, LOCAL-SPEC.md §9.6) until a `send` names the
attachment; the service records them (§10, `Pending`), releases them after the Message is written, and releases every
one still recorded at its next start, so an attachment no message named does not stay pinned for ever
(STRUCTURAL-DEBT.md 68).

## 9. Limits

| Bound | Value | When reached | Constant |
|---|---|---|---|
| messages per page (`timeline`, `thread`) | 256 | the page is cut, `more` | `ChatLimits.TimelinePage` |
| bytes of a reply or event | 1 MiB | the page is cut, `more` | `ChatLimits.PayloadBytes` |
| text | 16 384 bytes | `Invalid` before it is sent | `ChatLimits.TextLength` |
| attachments of a message | 64 | `Invalid` | `ChatLimits.Attachments` |
| attachment name | 255 bytes | `Invalid` | `ChatLimits.AttachmentNameLength` |
| preview of a `message` event | 256 bytes | cut between characters | `ChatLimits.PreviewLength` |
| jobs running | 16 | `Busy` | `ChatLimits.MaxJobs` |
| ended jobs remembered | 48 | the oldest forgotten | `ChatLimits.EndedJobsKept` |
| Channels listed | 256 | the list is cut | `ChatLimits.Channels` |
| `replyTo` steps to a thread's root | 64 | the walk stops | `ChatLimits.ThreadDepth` |
| job progress events | one a second | coalesced | `JobRule.ProgressInterval` |

## 10. Index

The index is a cache of what the Host stores (S-D2): rows in the service's own LDB database
(`<home>/services/chat/index`, the runtime's `ServiceState.Database`), rebuilt by store sequence. Schema 1:

| Table | Key | Holds |
|---|---|---|
| `ChatMeta` | `meta` | schema version, the store epoch the rows were built under, the store sequence of the last Entry taken in |
| `ChatChannels` | Package id | name, owner, followed, message and withdrawn counts, the last message's key, indexed |
| `ChatMessages` | Entry hash | Package (indexed), clock (indexed), device, stored, store sequence, text, attachments, `replyTo` (indexed), withdrawn and by whom, broken |
| `ChatAttachments` | attachment hash ‖ Entry hash | the attachment's hash and Package (indexed), name, size |
| `ChatRedactions` | Entry hash | target (indexed), Package (indexed), device, clock, store sequence |
| `ChatPending` | object hash | the attachment it was put for (indexed), when the job started |

A timeline is read through the clock index and the last clock of a page put in order in memory (LDB 0.1.0 streams an
order through one numeric column only). The runtime's cursor (`ServiceCursor`) shares the database. Rows built from
Entries and the cursor commit lazily durable (LDB `Durability.Lazy`): a crash loses only the latest commits, in log order,
so the cursor never stands past an Entry whose rows were lost, and the Entries come again; pending pins commit durably.
The text is a second copy of the Entry object's (STRUCTURAL-DEBT.md 66); the attachment key is concatenated
(STRUCTURAL-DEBT.md 67).

On connecting (`CatchUpPlan`): rows of the same epoch and schema are kept; rows of another store epoch or schema are
dropped, and the runtime replays from store sequence 1, or, when its cursor resumes further on, every followed Channel
is rebuilt from `ListHistory`. Then the Channels are made what `ListPackages` lists of the Channel kind. The catch-up
hands pages of up to 64 Entries to `OnEntriesAsync`, one index transaction each.

## 11. Rules

- **Order** (SPEC.md §9.3): messages line up by `(clock, deviceId, hash)`, identically on every device holding them.
- **Payload check** (PAYLOADS.md §1.2 to §4): a Message must parse to its end, its envelope's `references` must be its
  attachments' hashes each once and ascending, and `redacts` zero; else it is **broken**: placed in the timeline, never
  shown. A Redaction whose `target` differs from `redacts` is broken too, but the service follows the envelope, as the
  Host does.
- **Redactions** (SPEC.md §9.4): any Entry whose envelope's `redacts` is not zero is a redaction, kept in `ChatRedactions`
  whether its target is indexed or not, and applied when `Redactions.Honours` holds (same Channel, the target no
  redaction, from the target's device or a device of the owner, removed devices included); a redaction stored before
  its target applies when the target arrives.
- **Unknown Channels**: an Entry for a Channel the index does not know makes its row from `GetPackage` first (the topics
  are unordered against each other).
- **Package changes**: `Followed` makes or updates the row and takes in the history when the Host holds more Entries than
  the row has messages; a new Manifest renames; `Unfollowed` keeps the rows; `Dropped` removes them.
- **Replays**: every write is deduplicated by the Entry's hash; an Entry taken in twice changes nothing.

## 12. Conformance and scenarios

The rules above are pure functions in `Services/Axiom.Mesh.Chat/Rules` (`Timeline`, `TimelinePage`, `Threading`,
`SendRule`, `WithdrawRule`, `RedactionApplication`, `AttachmentRule`, `PayloadCheck`, `CatchUpPlan`, `JobRule`,
`AuthorResolution`), each tested from this document (`Chat.Tests: Rules/`). The scenario suite `ChatScenarios` runs
unchanged in memory and on real sockets with the real pipe: a Channel indexed on both ends; both sending with attachments
and showing the same keys in the same order; messages written offline slotting in by clock; a device withdrawing its
own; the owner withdrawing another's and a guest refused; a redaction stored before its target; attachments byte-equal
after `chat get`; a thousand messages paged forward and backward identically; a restart catching up across a gap over 256
without duplicates; another store epoch discarding the index; a thread surviving its root's withdrawal; unfollow, drop
and re-follow; `chat show --json` equal on both devices. A fourteenth scenario spawns the real executable from a real
daemon, kills it and sees it restarted. Rows in [CONFORMANCE.md](CONFORMANCE.md) under "Axiom.Mesh.Chat".

## 13. Test vectors

`Documentation/vectors/chat/records.txt` holds the payload of one sample of every record of §4 to §6 as hex
(`<record> <hex>`). An implementation decodes each and re-encodes it to the same bytes
(`Chat.Client.Tests: VectorTests`).

## 14. Security

The service runs as the Host's user and reads and writes only what an app names in `attach` and `get`, as that user; an
app that may open the pipe may already act as that user (LOCAL-SPEC.md §2.2). It holds no key: the Host signs every Entry
it writes. It shows nothing an honoured Redaction withdrew and nothing broken, and it refuses a withdrawal SPEC.md §9.4
would not honour. A peer's Entry it finds wrong is hidden in its namespace; it cannot keep the Host from storing or
forwarding it (LOCAL-SPEC.md §9.8).

## 15. Layouts at a glance

```
AboutReply:       namespace string(64), version string(64), storeEpoch id, indexedSeq u64, caughtUp bool, channels u32
ChannelsReply:    channels list<ChannelView>(256)
TimelineRequest:  packageId id, after optional<EntryKey>, reverse bool, limit u16
TimelineReply:    messages list<MessageView>(256), more bool
MessageRequest:   hash hash                      MessageReply:   message MessageView
ThreadRequest:    message hash, limit u16        ThreadReply:    root MessageView, replies list<MessageView>(256), more bool
SendRequest:      packageId id, text string(16384), attachments list<Attachment>(64), replyTo hash
SendReply:        key EntryKey                   WithdrawRequest: message hash      WithdrawReply: key EntryKey
AttachRequest:    path string(4096), name string(255)                              AttachReply:   job u32
GetRequest:       hash hash, message hash, path string(4096), existing u8          GetReply:      job u32
JobsReply:        jobs list<JobView>(64)
MessageEvent:     key EntryKey, packageId id, author AuthorView, preview string(256), attachments u16, replyTo hash
WithdrawnEvent:   key EntryKey, packageId id, by id
ChannelEvent:     change u8, channel ChannelView
JobEvent:         job JobView
```

## 16. Worked example

Alice sends a photo to the Channel "Family" with the command line; Bob fetches it.

```
alice$ axiom-mesh chat send Family --text "holiday" --file photo.jpg
  # subscribe to topic service (Filter.Service = chat), then
  # ServiceCall(chat, attach, AttachRequest{ path = /home/alice/photo.jpg })  -> AttachReply{ job = 1 }
  # event job { job 1, kind attach, state done, attachment { photo.jpg, <ChunkList>, 1048576 } }
  # ServiceCall(chat, send, SendRequest{ Family, "holiday", [photo.jpg], 0 })  -> SendReply{ key }
written: entry 3f2a… (clock 4)

bob$ axiom-mesh chat show Family
ENTRY         CLOCK  AUTHOR        STORED                MESSAGE
3f2a01b2c3d4  4      alice@laptop  2026-11-01 10:00:02Z  holiday [1 attachment: photo.jpg (1.0 MiB)]

bob$ axiom-mesh chat get 3f2a photo.jpg --out photo.jpg
  # ServiceCall(chat, get, GetRequest{ <ChunkList>, 3f2a…, /home/bob/photo.jpg, fail }) -> GetReply{ job = 1 }
  # the job fetches the ChunkList and its Chunks from Alice's laptop, then writes the file
written: /home/bob/photo.jpg (1.0 MiB)
```
