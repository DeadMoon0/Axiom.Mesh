# Axiom Mesh Local Protocol, Version 1 (axiom-mesh-local/1)

**Status:** Draft 2, 2026-10-28, after an adversarial review of draft 1 (43 findings, all addressed; the ones
that needed AMP/1 to change are in SPEC.md draft 3). Normative for the first implementation. Where this document and
[DESIGN.md](DESIGN.md) §10 differ, this document wins. Where it restates a rule of [SPEC.md](SPEC.md) (AMP/1),
SPEC.md wins and this document only says how that rule is reached over the pipe.

**Abstract.** `axiom-mesh-local/1` is the protocol every program on a device speaks to the Host, the one process
that holds the device's keys, rosters, connections, object store and database. It runs over a pipe the operating
system authenticates, carries typed binary frames with the framing and types of AMP/1, pairs requests with
responses by a correlation id, pushes events on subscriptions so that no client polls, reports long-running work
as operations, and has two roles: **user**, for the app, the command line and any local tool, and **service**,
for the programs that define a Package kind. A service never touches the wire, a key or the database; it writes
Entries the Host signs, stores and replicates, and reads verified objects from the store directory. The pipe
trusts the operating system's user: a process of the Host's user is that user, as with ssh-agent and Docker.

## Table of contents

1. Introduction
2. Transport
3. Framing, encoding and envelopes
4. Session
5. Records
6. User requests
7. Subscriptions and events
8. Operations
9. Service role
10. Telemetry
11. Errors
12. Limits
13. Security considerations
14. Registries
15. Conformance
Appendix A. Layouts at a glance
Appendix B. Worked example

## 1. Introduction

### 1.1 Requirements language

The key words MUST, MUST NOT, REQUIRED, SHALL, SHOULD, SHOULD NOT, RECOMMENDED, MAY and OPTIONAL are to be
interpreted as described in RFC 2119 and RFC 8174 when, and only when, they appear in all capitals.

### 1.2 Terminology

| Term | Meaning |
|---|---|
| **Host** | The one process on a device that holds the keys, rosters, discovery, every mesh connection, the object store and the database (DESIGN.md §10.2). The Host is the AMP/1 node of the device. |
| **Client** | A program connected to the Host over the pipe, in either role. |
| **User client** | A client in the user role (§4.2, §14.2): the app, the CLI, a script. |
| **Service** | A client in the service role: a program that defines a Package kind and the Entry kinds in it (§9). |
| **Pipe** | The operating-system transport between a client and the Host (§2). |
| **Connection** | One pipe connection. One connection carries one session (§4). |
| **Request**, **Response** | A message from a client to the Host, and the Host's one answer to it, paired by a correlation id (§3.4). |
| **Event** | A message the Host pushes on a subscription (§7). |
| **Callback**, **Reply** | A message from the Host to a service that needs an answer, and that answer, paired by a callback id (§3.6). |
| **Subscription** | A client's standing request for the events of one topic, with a filter (§7). |
| **Operation** | Long-running work the Host does for a client, reported by events until it ends (§8). |
| **Store sequence** | A counter the Host increments by one for every Entry it stores; every stored Entry carries the value it got (§5.6). |
| **Store epoch** | A random id created with the Host's database and changed whenever the database is restored, reset or recreated; it tells a client that store sequences started over (§4.2, §9.3). |
| **Process identity** | What the operating system tells the Host about the process at the other end of a connection: its OS user, its process id and, where the platform provides it, its executable path (§2.2). |
| **Held**, **Stored**, **Object**, **Entry**, **Head**, **Package**, **Grant**, **Roster**, **Known user**, **Followed Package**, **Listed Package** | As in SPEC.md §1.2. In this document **held** means only "in the held buffer"; where the text says a Host or client *holds* an object, it means the object is stored. |

### 1.3 Notation

Byte layouts are written as `name: type` lists in wire order. Types are those of SPEC.md §5.2 plus the ones
defined in §3.2. All multi-byte integers are big-endian. Sizes use binary units. Hex is lowercase. A request's
section shows its body under the request's name and the body of its OK response under **OK**; "empty" means a
body of zero bytes.

### 1.4 Relation to AMP/1

The pipe never carries an AMP/1 frame, and no client is an AMP/1 node: the Host is the only node on the device.
Objects, Entries, Manifests, Rosters and Grants are those of SPEC.md §8. They reach a client either as their
bytes (`GetObject`, §6.4) or as **records** (§5) the Host derives from objects it has verified under SPEC.md. A
client verifies nothing and signs nothing: the Host verifies every object before a client can see it, and the
Host signs every Entry a service writes with the device key (§9.4). Everything a client may do, the Host does
under the rules of SPEC.md §9 and §11; this document names the request, SPEC.md names the rule.

### 1.5 Versioning and extensions

This is version 1 of the local protocol. The version is carried in `Hello` and `HelloAck` (§4.2); the protocol
name `axiom-mesh-local/1` is the entry protocol of the Host's local port (DESIGN.md §10.2). A Host MUST refuse
a client whose `version` it does not support with `Bye(Refused)` (§4.2).

Within version 1, extensions MAY add requests, events, callbacks, topics, operation kinds, pin reasons,
telemetry series and status codes from the reserved ranges of §14. So that a 1.0 client and a 1.x Host
interoperate, and the reverse:

- a request whose message id is in the request range (§3.3) and unknown to the Host is answered with status
  `UnknownRequest` (§11); the connection stays open;
- an event whose message id is unknown to the client MUST be ignored (read and discarded);
- a callback whose message id is unknown to the service MUST be answered with `Reply(UnknownRequest)`;
- a frame whose message id is in the session range and unknown, or in no range at all, is a violation (§11.3);
- an unknown capability flag is ignored; an unknown topic, operation kind, pin reason or enumerated value in a
  request is answered with status `Invalid`;
- an enumerated value a client does not know in a `Response`, `Event` or `Callback` (a change kind, a pin
  reason, a peer state, an operation kind, a status code) MUST be tolerated: the client reads the message, treats
  the value as "other" and keeps the connection; only a structural bound (§3.2) is a Host defect;
- a field is never added to an existing layout; a changed layout is a new message with a new id.

Anything that changes the meaning of an existing byte is version 2.

### 1.6 Decisions

Where DESIGN.md §10 leaves something open, this document decides it. The decisions, so that DESIGN.md can be
brought in line:

1. **Message classes by id range** (§3.3), so that an unknown request gets an error response and not a closed
   connection, and so that the Host knows a frame's class before it knows its message.
2. **Who chooses ids.** Correlation ids and subscription ids are chosen by the client; operation ids and callback
   ids by the Host (§3.2).
3. **A service gets its Entry stream through subscriptions** (§9.3), the same mechanism user clients use, with a
   **store sequence** (§5.6) for catch-up after a restart or a lapsed subscription. The `Entries` table of
   DESIGN.md §13 gains a `storeSeq` column, unique and increasing.
4. **A fourth pin reason, `Service`** (§9.6, §14.8), for objects a service put or asked to keep, and a fifth,
   `Outbound`, held by the Host on the objects an Entry written here references until a peer has fetched them
   (§9.4, decision 22).
5. **The Host parses the built-in payload layouts itself** (§9.4, §9.8): for Entry kinds 1 to 3 it checks the
   layout of SPEC.md §8.5 parses exactly to its end and takes the objects the payload references from that
   parse; a payload that does not parse is faulty content (SPEC.md §8.5, §12.2) and never stored. No service is
   asked to validate a payload, no Entry waits for a service, and an Entry of an extension kind is stored
   opaquely with the references its writer named in `WriteEntry`. Draft 1's `ValidatePayload` callback is gone;
   its id stays reserved (§14.1).
6. **Parents when the service names none** (§9.4): the rule of SPEC.md §9.3, which this document only restates:
   the device's own Head; when the device has no Head but holds Entries of the Package, the Entry highest in
   the total order; a second parent when the clock gap rule of SPEC.md §8.6 would otherwise be broken.
7. **Manifests, Grants and Rosters need the user key**, which lives on one device (DESIGN.md §5.1). A request that
   needs it on a device that lacks it fails with `UserKeyAbsent` (§11), and the app directs the user to the
   device that holds it.
8. **Enrolment of a second device is a pairing mode** (§6.2): the same requests and states as pairing two users,
   with modes `ShowDevice` and `AddDevice`.
9. **macOS socket path** `$TMPDIR/axiom-mesh.sock`, and the environment variable `AXIOM_MESH_PIPE` overrides the
   pipe name on every desktop platform (§2.1), for tests and for several Hosts on one machine.
10. **A second connection with a live service's name is refused** with `Bye(Duplicate)`; it does not replace the
    first (§4.2).
11. **Liveness is responsiveness, not activity** (§4.4): a client that answers `Ping` is never closed for being
    quiet; a client that does not answer within 30 seconds is.
12. **Telemetry keys** are strings of at most 192 bytes so that a peer and a Package fit in one key; gauges have
    no lifetime total, `GetTotal` returns their last value (§10).
13. **The Host interprets the built-in layouts and nothing else** (decision 5). It applies `Redaction`s itself
    and marks a withdrawn `Message` with `Entry.redacted` (§5.6); `GetEntry` and `ListHistory` still return the
    payload bytes, and every user client MUST honour the flag, which is how the "MUST NOT show" of SPEC.md §9.4
    is met for the CLI and for any script, not only for the app.
14. **Topics `users` and `store`** exist because the app shows known users, rosters and the store budget live.
15. **Objects on demand** (DESIGN.md §7.3) are fetched by the `Fetch` operation (§6.4, §8.6); `Materialise`
    fetches what it lacks itself.
16. **A Package of any kind may be created**, with or without a service for that kind; the Host carries unknown
    kinds opaquely as SPEC.md §9.2 says.
17. **`GetStorePath` is a service request** (§9.7); user clients read objects through `GetObject`.
18. **Local retention policy** per Package is a value of the same enumeration as the Manifest's `retention`,
    with 0 meaning "the Manifest's value, else the device class" (§14.14).
19. **Live telemetry is per minute by default**; a client that sets capability bit 0 also receives partial
    samples of the current minute at most once per second (§10.4).
20. **A listed Package is a state of its own** (§5.4, §14.12): a Package a peer listed to this node but that it
    does not follow has `state` Listed and, once the Host fetched its Manifest (SPEC.md §10.6, draft 3), a name
    and a kind the user can decide on; `PackageChanged(Listed)` tells the app it appeared.
21. **Rosters are pushed** (§6.1, §6.3): a new Roster of the own user reaches every established peer as an
    unsolicited `Object` (SPEC.md §7.6, draft 3), not only on the next `Hello`.
22. **A fifth pin reason, `Outbound`** (§9.4, §14.8): the Host keeps every object an Entry written on this device
    references until it has sent the object to at least one peer, whatever the retention policy, so that a
    phone cannot evict an attachment nobody else has yet.
23. **The pipe checks process identity** (§2.2): the Host refuses a connection whose OS user is not the user it
    serves, logs user, process id and executable of every connection, MAY confine the user-key requests (§6) to
    configured executables, and resolves every path a client names as that client's user (§13). A spawned
    out-of-process service proves it was spawned with `Hello.token` (§4.2); an in-process service's name is never
    granted on the pipe.
24. **The store has an epoch** (§4.2, §9.3): `HelloAck.storeEpoch` changes when the database was restored or
    recreated, so a client that kept its own index knows that store sequences started over.

## 2. Transport

### 2.1 The pipe

| Platform | Pipe | Name |
|---|---|---|
| Windows | a named pipe, byte mode, duplex, one instance per connection | `\\.\pipe\axiom-mesh` |
| Linux | a Unix domain socket, `SOCK_STREAM` | `$XDG_RUNTIME_DIR/axiom-mesh.sock`, else `/run/user/<uid>/axiom-mesh.sock` |
| macOS | a Unix domain socket, `SOCK_STREAM` | `$TMPDIR/axiom-mesh.sock` |
| Android | an in-memory stream pair inside the app process, adopted into the local port | none |
| iOS | an in-memory stream pair inside the app process, adopted into the local port | none |

On Windows, Linux and macOS the environment variable `AXIOM_MESH_PIPE`, when set and non-empty in the Host's
and the client's environment, replaces the name in the table; both MUST honour it. A client MUST NOT guess any
other name. The pipe carries bytes in both directions with no framing of its own; §3 defines the framing.

The Host creates the pipe so that nobody else can stand in for it, and so that it is reachable from this
machine only:

- on Windows the named pipe MUST be created with `FILE_FLAG_FIRST_PIPE_INSTANCE` and `PIPE_REJECT_REMOTE_CLIENTS`;
  a Host whose creation fails because the name exists MUST NOT start, and MUST log it;
- on Linux and macOS a socket file left by a crashed Host is removed before binding only after a connection
  attempt to it was refused; a file that answers belongs to a running Host, and the new one MUST NOT start;
- a client SHOULD verify, where the platform offers it (`GetNamedPipeServerProcessId`, `SO_PEERCRED`,
  `LOCAL_PEERCRED`), that the process serving the pipe runs as the user the client expects, and MUST treat a
  pipe name taken from `AXIOM_MESH_PIPE` as exactly as trusted as whoever set that variable.

### 2.2 Authentication

The operating system authenticates. The named pipe's access control list, or the socket file's owner and mode,
admit the OS user the Host serves and nobody else, and the Host MUST, for every connection, obtain the
**process identity** of the peer where the platform provides it: its OS user, its process id and its executable
path (Windows: `GetNamedPipeClientProcessId` and the image path of that process; Linux: `SO_PEERCRED` and
`/proc/<pid>/exe`; macOS: `LOCAL_PEERCRED` and `LOCAL_PEERPID` with `proc_pidpath`). The Host MUST refuse the
connection, by closing it before `Hello` is answered, when the peer's OS user is not the user the Host serves,
or, for a Host that runs as a system service, not a user in its configured allow list; the access control list
is the first fence, this check is the second.

A process of the same OS user is trusted as that user for everything the user role may do, as with ssh-agent
and Docker Desktop: the protocol adds no finer right, and claims none. There is no password and no network
port, and the one token there is (§4.2) proves that a service was spawned by the Host, not who the user is.

Two things are built on the process identity beyond admission. The requests that need the user's private key
(§6: `ConfirmPairing` in modes `ShowUser`, `JoinUser` and `AddDevice`, `SetGrant`, `RevokeGrant`, `RemoveDevice`
and `ForgetUser`) are the **user-key requests**; the Host's configuration MAY confine them to listed executable
paths, and a user-key request from any other process is answered `NotPermitted`. Every user-key request MUST
be logged with the process id and path of the connection that sent it, whether it succeeded or not. And every
path a client names (§13) is resolved as that client's user.

### 2.3 Connection lifetime

A connection begins when the client connected to the pipe and ends when either side closes it. The client MUST
send `Hello` within 10 seconds of connecting; the Host closes the connection without a frame otherwise. The Host
MUST NOT keep more than 16 connections waiting for `Hello` at once; when a 17th arrives it closes the one that
has waited longest. When a connection ends,
for whatever reason, everything tied to it ends with it: outstanding requests are never answered, subscriptions
are gone, running operations are cancelled (§8.5), outstanding callbacks are abandoned, and a service's
registered kinds are released (§9.2). Nothing of a connection survives it; the Host keeps no session state
across connections.

A client that loses its connection SHOULD reconnect, waiting 1 second after the first failure and doubling up to
30 seconds, and then re-establish what it needs: subscriptions, and for a service its registration (§9.2). The
Host MUST accept up to 64 user connections at once and one connection per configured service name (§12). A
service whose `Response` to `WriteEntry` was lost with the connection MUST catch up (§9.3) before it retries
the write: `WriteEntry` is not idempotent, and the same payload written twice is two Entries.

### 2.4 Permissions

The exact access control list of the named pipe, the owner and mode of the socket file and its directory, the
account the Host serves when it runs as a Windows service, its allow list of users (§2.2), and the configuration
keys that set all of these are specified in a separate document, `PIPE-ACCESS.md` (to be written); this document
requires the effect stated in §2.1 and §2.2 and nothing of the form.

## 3. Framing, encoding and envelopes

### 3.1 Frames

Every message is one frame, laid out exactly as in SPEC.md §5.1:

```
length:     u32        number of bytes that follow, i.e. 2 + len(payload)
messageId:  u16        from the registry in §14.1
payload:    bytes      the message body, len = length - 2
```

`length` MUST be at least 2 and at most 4 198 400 (4 MiB + 4 KiB). A frame outside these bounds is a violation
(§11.3). A receiver MUST NOT allocate more than `length` bytes for a frame before it has received them. A frame
MUST be received completely within 30 seconds of its first byte; the Host closes the connection with
`Bye(Violation)` otherwise, and a client treats it as a lost connection.

### 3.2 Types

The types of SPEC.md §5.2 (`u8` to `u64`, `bool`, `bytes<n>`, `hash`, `id`, `pubkey`, `signature`,
`string(max)`, `blob(max)`, `list<T>(max)`, `biglist<T>(max)`) are used unchanged. New in this document:

| Type | Encoding |
|---|---|
| `correlation` | `u32`, never 0, chosen by the client (§3.4) |
| `subscription` | `u32`, never 0, chosen by the client (§7.1) |
| `operation` | `u32`, never 0, chosen by the Host (§8.1) |
| `callback` | `u32`, never 0, chosen by the Host (§3.6) |
| `time` | `u64`, milliseconds since the Unix epoch; 0 means "unknown" where a field says so |
| `optional<T>` | `u8` 0 or 1, then `T` only when the byte is 1; any other first byte is malformed |
| `path` | `string(4096)`; an absolute path in the platform's native form; MUST NOT be empty or relative |
| `name` | `string(64)` of ASCII letters, digits, `.`, `-` and `_`, never empty; a client, service, method, event or series name. A service name is at most 32 bytes (§4.2, §9.9) |
| `object` | `blob(4194304)`; the bytes of one SPEC.md §8 object |
| `payload` | `blob(1048576)`; the payload bytes of one Entry (SPEC.md §8.5), or bytes of a service's own that the Host carries uninterpreted |

A type's **bounds** are its length, count and width, the `0`/`1` of a `bool` and of an `optional<T>`'s first
byte, and valid UTF-8 in a `string`. A value outside its bounds in a request or a `Reply` is a violation
(§11.3); in a `Response`, `Event` or `Callback` it is a Host defect the client treats as a lost connection.
What a type says beyond its bounds (the character set of a `name`, that a `path` is absolute, that an
enumerated field holds a defined value) is **substance**: a request that breaks it is answered `Invalid`, and
a client that receives an enumerated value it does not know tolerates it (§1.5). The canonical form of
SPEC.md §5.3 applies: fixed field order, no padding, every list in the order its definition states, and
`optional<T>` is the only conditional field.

### 3.3 Message classes

The message id says which class a frame belongs to, so the receiver knows how to read the envelope before it
knows the message:

| Class | Ids | Sent by | Envelope (the first bytes of the payload) |
|---|---|---|---|
| Session | 1 to 31 | both | none |
| Response | 32 | Host | `correlation: correlation  status: u16  body` |
| Reply | 33 | a service | `callback: callback  status: u16  body` |
| (reserved) | 34 to 255 | — | a violation when received |
| Request | 256 to 1023: general requests 256 to 511 (§6, §7, §8, §10; each states its roles), service requests 512 to 767 (§9), extensions 768 to 1023 | a client | `correlation: correlation  body` |
| Event | 1024 to 1535 | Host | `subscription: subscription  body` |
| Callback | 1536 to 2047 | Host | `callback: callback  body` |
| (never assigned) | 2048 to 65535 | — | a violation when received |

A client MUST NOT send a Response, Event or Callback; a user client MUST NOT send a Reply; the Host never sends a
Request or a Reply. A frame of a class its sender may not send is a violation.

### 3.4 Requests and responses

A request is a frame in the request range whose payload is a `correlation` followed by the body its section in
§6, §7, §8, §9 or §10 states. The Host answers every request with exactly one `Response` (32) carrying the same
`correlation`: with `status` 0 (`OK`) and the OK body the request's section states, or with a non-zero `status`
from §14.9 and an `Error` record (§5.14) as the body. A request that fails has no effect on the Host's state;
`ServiceCall` (§6.7) is the exception, since the Host cannot undo what a service did before it answered.

Rules:

- `correlation` MUST NOT be 0 and MUST differ from every correlation of a request on the same connection whose
  `Response` has not yet been sent; a client MAY reuse a correlation once its `Response` arrived. A request that
  breaks this is a violation.
- A client MUST NOT have more than 64 requests outstanding on one connection; the 65th is a violation.
- The Host MAY process requests concurrently and MAY answer them in any order. Two requests on one connection
  that write to the same Package are applied in the order they were received. A client that needs one request's
  effect to be visible to another MUST wait for the first `Response`.
- A request whose message id is in the request range but unknown to the Host is answered with
  `UnknownRequest`; one that the connection's role may not send, with `NotPermitted` (each request's section
  states the roles).
- The Host SHOULD send the `Response` within 10 seconds; work that can take longer is an operation (§8), whose
  starting request is answered at once, and before the first event of that operation (§7.2).
- Nothing is stated about the order between a `Response` and the events on the same connection, except the two
  rules above and those of §7.1. A client that wants a complete picture subscribes first, then lists, and
  reconciles the listing with the events by `manifestSeq`, `rosterSeq`, `storeSeq` or the hashes they carry.
- While the session is Draining (§4.5) the Host answers every new request with `Draining`, except
  `Unsubscribe`, `CancelOperation`, `GetOperation` and `ListOperations`, which it still serves.

### 3.5 Events

An event is a frame in the event range whose payload is the `subscription` it belongs to, followed by the body
its section in §7.3 states. Events flow from the Host only, only on a live subscription of that connection, and
only after the `Response` to its `Subscribe` was sent (§7.1). Events of one subscription are delivered in the
order §7.2 states for its topic; nothing is stated about the order between subscriptions.

### 3.6 Callbacks and replies

A callback is a frame in the callback range from the Host to a service, whose payload is a `callback` id the
Host chose, unique among the callbacks outstanding on that connection, followed by the body its section in
§9.8 states. The service MUST answer each callback with exactly one `Reply` (33) carrying the same id, a
`status` from §14.9 (0 for success) and the reply body the callback's section states, or an `Error` record
(§5.14) when `status` is not 0. Every callback has a deadline its section states; at the deadline it leaves the
outstanding set and the Host acts as if it had failed. A `Reply` naming a callback that expired within the last
5 minutes is ignored; a `Reply` naming an id the Host never issued on this connection is a violation. The Host
MUST NOT have more than 64 callbacks outstanding toward one service; §9.8 says what it does past that.

## 4. Session

### 4.1 States

```
            Hello / HelloAck                 Drain
Connected ───────────────────► Ready ─────────────────► Draining
    │                            │                          │
    └──── Bye or close ◄─────────┴────────── Bye ◄──────────┘
```

- **Connected:** the pipe connection exists. The client MUST send `Hello` as its first frame; the Host accepts
  nothing else from it but `Bye`, and sends nothing before `Hello` arrived. Any other first frame is a
  violation; no `Hello` within 10 seconds of the connection closes it without a frame (§2.3). The Host answers
  `HelloAck` or `Bye`.
- **Ready:** `HelloAck` was sent. Requests, responses, events, callbacks, replies, `Ping`, `Pong` and `Bye` may
  flow. A second `Hello` is a violation.
- **Draining:** the Host sent `Drain` (§4.5). Everything of Ready still flows under the rule of §3.4; the
  client is expected to send `Bye`.

`Bye` is accepted in every state; a malformed `Bye` is treated as a close.

### 4.2 `Hello` (1) and `HelloAck` (2)

```
Hello:
  version:        u16             = 1
  role:           u8              §14.2: 1 user, 2 service
  name:           name            service role: the service's name; user role: the client's name, for logs
  clientVersion:  string(64)      for logs; MUST NOT be interpreted
  capabilities:   u32             bit flags, §14.4 (client flags)
  token:          bytes<32>       service role, spawned by the Host: the token it was given; else all zero
HelloAck:
  version:        u16             = 1
  hostVersion:    string(64)      for logs; MUST NOT be interpreted
  role:           u8              the role granted; equals Hello.role
  capabilities:   u32             bit flags, §14.4 (Host flags), bits 0 and 1 as they hold for this connection
  identity:       Identity        §5.1; present = false before CreateIdentity or enrolment
  storeEpoch:     id              the store epoch (§1.2, §9.3)
  storeSeq:       u64             the store sequence of the newest stored Entry; 0 when none
```

The Host's configuration lists every service by **name** (at most 32 bytes, §3.2), with the Package kinds and
Entry kinds it may register (§9.2), whether it runs **in-process** (DESIGN.md §10.3) and, for an out-of-process
service, the executable the Host **spawns** and supervises. When the Host spawns a service it puts a fresh
random token of 32 bytes into the child's environment as `AXIOM_MESH_SERVICE_TOKEN`, 64 lowercase hex
characters, and expects it back in `Hello.token`; a token is valid for one connection of that child and until
the child exits. In-process services connect to the local port over an in-memory stream pair before the Host
opens the pipe, with `token` all zero.

On `Hello` the Host checks, in this order, and on the first failure sends `Bye` with the stated reason and
closes:

1. `version` is 1; else `Bye(Refused, "version")`.
2. `role` is 1 or 2; else a violation, `Bye(Violation)`.
3. For role 2, `name` is a service name the Host's configuration lists, the name is not configured as
   in-process unless the connection *is* the in-memory pair of that in-process service, and for a service the
   Host spawns `token` equals the token it gave that child; else `Bye(Refused, "service")`. A client the
   configuration does not list, or that cannot prove the name, is refused: the Host MUST NOT grant a role other
   than the one asked for.
4. For role 2, no other connection with the same `name` is in Ready or Draining; else `Bye(Duplicate)`. The
   live connection is kept.
5. The connection limits of §12 hold; else `Bye(Refused, "busy")`.

Then the Host sends `HelloAck` and the session is Ready. Apart from step 3, an in-process service is treated
exactly like a program on the pipe.

### 4.3 `Bye` (3)

```
reason:     u8                  §14.3; other values are read as 0, Unspecified
detail:     string(256)         for logs; MUST NOT be interpreted
```

Either side sends `Bye` and then closes. A receiver of `Bye` MUST NOT send further frames and SHOULD close. A
connection may also simply end; both sides MUST handle that. A client that is done sends `Bye(Done)`.

### 4.4 `Ping` (4) and `Pong` (5)

```
nonce:      u64
```

In Ready and Draining, the Host MUST send `Ping` when it has received nothing from the client for 60 seconds and
the client MUST answer with `Pong` carrying the same nonce within 30 seconds, else the Host closes the
connection with `Bye(Unresponsive)`. A client MAY send `Ping` at any time in Ready or Draining and the Host MUST
answer `Pong` with the same nonce. A `Pong` with an unknown nonce is ignored. A client that answers is never
closed for being quiet.

### 4.5 `Drain` (6)

```
grace:      u32                 milliseconds the Host will wait, 1 to 30 000
```

Sent by the Host, to every connection, when it is about to stop (shutdown, or the local port retiring). The
client SHOULD finish what it can within `grace` and send `Bye(Done)`. Requests that arrive after `Drain` are
answered `Draining` (§3.4); running operations are cancelled (§8.5) and their final events delivered; events
already queued are delivered while the grace lasts. When `grace` elapsed the Host sends `Bye(Shutdown)` and
closes. The default grace is 5 000 milliseconds.

## 5. Records

Records are structures several messages share. Every record is encoded exactly as laid out here; every list in a
record has the stated order. Records are what the Host derives from verified objects and from its database; a
client never sees an unverified object through a record.

### 5.1 `Identity`

```
present:        bool            false when this device has no identity yet; then every other field is zero or empty
userId:         id
userName:       string(64)      the user's display name, local to this device's database
userKeyPresent: bool            the user's private key is on this device
deviceId:       id
deviceName:     string(64)      the name in the current Roster
rosterSeq:      u64             the sequence number of the current Roster of the own user
created:        time            when the identity was created or enrolled on this device
```

### 5.2 `User`

```
userId:         id
name:           string(64)      display name, local to this device's database
self:           bool            this is the own user
knownSince:     time
rosterSeq:      u64             the current Roster held for this user; 0 when none
devices:        u16             devices in that Roster that are not removed
```

### 5.3 `Device`

```
deviceId:       id
userId:         id
name:           string(64)      from the Roster
addedAt:        time            from the Roster; informational
removed:        bool
self:           bool            this device
lastSeen:       time            the end of the last established session with it; 0 when never
```

### 5.4 `Package`

```
packageId:      id
state:          u8              1 Listed: a peer lists it to this node, it is not followed; 2 Followed
ownerId:        id
kind:           u16             SPEC.md §14.8; unknown kinds are carried
name:           string(255)     from the current Manifest
created:        time            from the current Manifest
retention:      u8              from the current Manifest: 0 default, 1 everything, 2 latest only
manifestSeq:    u64
manifestHash:   hash
branches:       list<Branch>(64)    SPEC.md §8.7, ascending by name
listedBy:       u16             established peers that currently list the Package to this node
policy:         u8              the local retention policy, §14.14
rights:         u8              this user's rights: 2 for the owner, else the current Grant's rights, else 0
heads:          list<Head>(256) SPEC.md §7.2, ascending by deviceId; the Heads this Host has stored
entries:        u64             stored Entries of this Package
latestClock:    u64             the highest clock among them; 0 when none
```

`Branch` and `Head` are the structures of SPEC.md §8.7 and §7.2, encoded as there. A Package in state Listed
whose Manifest the Host has not yet fetched (SPEC.md §10.6) has `ownerId` all zero, `kind` 0, `name` empty,
`created` 0, `retention` 0, `manifestSeq` 0, `manifestHash` all zero and `branches` empty; once the Manifest
is held, those fields are filled and `rights` is known. `heads`, `entries` and `latestClock` are those of the
objects stored here, which for a Listed Package are the leftovers of an earlier follow, or nothing. A Package
the own user owns is always Followed.

### 5.5 `Grant`

```
hash:           hash            the Grant object
packageId:      id
granteeId:      id
rights:         u8              SPEC.md §8.9: 0 none, 1 read, 2 write
seq:            u64
```

### 5.6 `Entry` and `EntryKey`

```
Entry:
  hash:         hash            the Entry object
  storeSeq:     u64             the store sequence, §1.2: 1 for the first Entry this Host ever stored, then +1
  stored:       time            when this Host stored it
  packageId:    id
  deviceId:     id
  clock:        u64
  parents:      list<hash>(2)   ascending as byte strings, as in the object
  kind:         u16             SPEC.md §14.7
  payload:      payload         the Entry's payload bytes, as in the object
  redacted:     bool            a stored Redaction that SPEC.md §9.4 honours names this Entry
EntryKey:
  clock:        u64
  deviceId:     id
  hash:         hash
```

An `EntryKey` is the position of an Entry in the total order of SPEC.md §9.3, `(clock, deviceId, hash)`
ascending. The store sequence is the position in the order this Host stored Entries, across every Package; it
is unique, increasing, never reused, and the Host MUST persist it with the Entry. An Entry that was evicted
and is stored again gets a new store sequence, so one hash can appear twice in a store-sequence walk; a client
that indexes Entries MUST de-duplicate by `hash`. `redacted` is set by the Host from the Redactions it holds
(decision 13); a user client MUST NOT display the payload of a redacted Entry.

### 5.7 `Peer`

```
deviceId:       id              as claimed by discovery, or proven by a session
userId:         id              as claimed, or proven; all zero when unknown
known:          bool            deviceId is a device, not removed, in a Roster this Host holds
addresses:      list<string(256)>(8)  host:port, ascending as byte strings; the most recent 8
sources:        u8              bit flags, §14.11
firstSeen:      time
lastSeen:       time
state:          u8              §14.11
backoffUntil:   time            0 when not backing off
violations:     u32             violations recorded against the device in the last hour
```

### 5.8 `Session`

```
deviceId:       id
userId:         id
direction:      u8              1 inbound (accepted), 2 outbound (dialled)
remote:         string(256)     the peer's address, host:port
local:          string(256)     this node's address for the session, host:port
since:          time            the TCP connection
lastReceived:   time
tlsSuite:       string(64)      the negotiated cipher suite name
state:          u8              1 Connected, 2 Established, 3 Switched (SPEC.md §6.1)
service:        string(64)      the service protocol name while Switched; else empty
bytesIn:        u64
bytesOut:       u64
objectsIn:      u64
objectsOut:     u64
wantsOut:       u16             hashes this node has outstanding toward the peer (SPEC.md §7.5)
wantsIn:        u16             hashes the peer has outstanding toward this node
```

### 5.9 `StaticPeer`

```
address:        string(256)     host:port as configured
lastDialled:    time            0 when never
lastResult:     u8              0 never dialled, 1 established, 2 refused, 3 unreachable
```

### 5.10 `Store`

```
path:           path            the store directory
objects:        u64
bytes:          u64             of stored objects
budget:         u64             0 = the device default
pinnedBytes:    u64             bytes of objects with at least one pin
evictableBytes: u64             bytes of objects with no pin
heldBytes:      u64             bytes in the held buffer (SPEC.md §10.6)
```

### 5.11 `Pin` and `PinKey`

```
Pin:
  hash:         hash
  reason:       u8              §14.8
  source:       id              Head, Policy, Outbound: the Package id (Head on a Roster: the user id); User: all zero; Service: H(service name) over the name's UTF-8 bytes
  since:        time
  size:         u64             the object's size; 0 when the object is not stored
PinKey:
  hash:         hash
  reason:       u8
  source:       id
```

Pins are ordered by `(hash, reason, source)` ascending as byte strings.

### 5.12 `Service`

```
name:           name
connected:      bool
inProcess:      bool
version:        string(64)      from its Hello; empty when not connected
spawned:        bool            the Host spawns and supervises it (§4.2)
kinds:          list<u16>(16)   Package kinds its configuration lets it register, ascending
entryKinds:     list<u16>(64)   Entry kinds its configuration lets it register, ascending
```

### 5.13 `Operation`

```
operation:      operation
kind:           u16             §14.6
state:          u8              §14.7
started:        time
updated:        time            the last change of any field below
done:           u64             progress, in units
total:          u64             0 when unknown
unit:           u8              1 bytes, 2 objects
message:        string(256)     for display; MUST NOT be interpreted
status:         u16             §14.9: the reason when state is Failed, Cancelled when state is Cancelled; else 0
result:         blob(4096)      the kind's result body (§8.6) once state is Done; else empty
```

### 5.14 `Error`

```
message:        string(256)     for display and logs
detail:         string(1024)    for logs; MUST NOT be interpreted
```

The body of every `Response` and `Reply` whose `status` is not 0.

### 5.15 `Pairing`

```
pairing:        u32             never 0; one per pairing
mode:           u8              §14.10
state:          u8              §14.10
code:           string(512)     what this side shows (text, also encodable as a QR code); empty unless this side shows
sas:            string(64)      the short authentication string, once both sides are connected; else empty
remoteUserId:   id              the other side's user; all zero until known
remoteName:     string(64)      the other side's display name; empty until known
remoteDeviceId: id              the other side's device; all zero until known
expires:        time
failure:        string(256)     when state is Failed; else empty
```

The exchange behind these fields is `axiom-mesh-pair/1` (SPEC.md §14.4), a separate document; `code` and `sas`
are opaque to the client and shown as they are.

### 5.16 `Transfer`

```
peer:           id              the peer device
packageId:      id              the Package the object was wanted through; all zero for a Roster
hash:           hash
direction:      u8              1 in (this node wants or received), 2 out (the peer wants or was sent)
size:           u64             the object's size; 0 when unknown
state:          u8              1 wanted, 2 received and stored, 3 sent, 4 not found, 5 cancelled, 6 dropped (held buffer or faulty)
at:             time
```

### 5.17 `TelemetrySeries` and `TelemetryPoint`

```
TelemetrySeries:
  name:         name            §14.13
  kind:         u8              1 counter, 2 gauge
TelemetryPoint:
  minute:       time            the start of the minute, a multiple of 60 000
  value:        u64             counter: the sum within the minute; gauge: the last value within the minute
```

### 5.18 `Filter`

```
packageId:      id              all zero = any
deviceId:       id              all zero = any
userId:         id              all zero = any
kinds:          list<u16>(16)   empty = any; Package kinds or Entry kinds as the topic says; ascending
operation:      u32             0 = any
service:        string(64)      empty = any
series:         string(64)      empty = any
key:            string(192)     empty = any
fromSeq:        u64             0 = live only; else replay from this store sequence (§7.2)
```

Which fields a topic reads is stated in §7.2; a field a topic does not read MUST be zero or empty, else
`Subscribe` fails with `Invalid`.

## 6. User requests

Every request below lists its message id and the roles that may send it: **U** the user role, **S** the service
role. A request sent by a role not listed is answered `NotPermitted`. Every request may fail with `Draining`,
`Busy` (§12) or `Invalid` (a value that is in bounds but wrong in substance, named in `Error.detail`); only
further errors are listed. Requests that need an identity fail with `NoIdentity` before any other check when
`Identity.present` is false; those are `RenameDevice`, `StartPairing` in every mode but `ShowDevice`,
`ForgetUser`, every request of §6.3 and §6.4, `Pin`, `Unpin`, `ReleasePins`, `Dial`, `ServiceCall`, and
`WriteEntry` (§9.4). The **user-key requests** of §2.2 (`ConfirmPairing` in modes `ShowUser`, `JoinUser` and
`AddDevice`, `SetGrant`, `RevokeGrant`, `RemoveDevice`, `ForgetUser`) are logged with the connection's process
identity and may be confined by configuration to listed executables (`NotPermitted` from any other).

Lists that have no cursor (`ListKnownUsers`, `ListDevices`, `ListGrants`, `ListPeers`, `ListSessions`,
`ListDiscovered`, `ListStaticPeers`, `ListServices`, `ListSeries`) hold the first elements in their stated
order, cut to the list maximum and to the frame limit of §3.1; a Host that had to cut SHOULD log it. Lists
with a cursor (`after`) are cut the same way and say so with `more`.

### 6.1 Identity

```
GetIdentity (256), U S:                 empty
  OK:   identity: Identity

CreateIdentity (257), U:
  userName:     string(64)      MUST NOT be empty
  deviceName:   string(64)      MUST NOT be empty
  OK:   identity: Identity

RenameDevice (258), U:
  deviceId:     id              a device of the own user, not removed
  name:         string(64)      MUST NOT be empty
  OK:   device: Device
```

`CreateIdentity` creates the user key, a Roster with `seq` 1 holding this device, and the device certificate
(SPEC.md §2.4); it creates the device key too unless an earlier `ShowDevice` pairing (§6.2) already created
one, which it then uses. It fails with `AlreadyExists` when an identity is present; a device that is to join
an existing user enrols instead (§6.2). `RenameDevice` produces a new Roster with `seq` one higher and pushes
it to every established peer as SPEC.md §7.6 allows for the own user's Roster; it needs the user key
(`UserKeyAbsent`), fails with `NotFound` for a device that is not the own user's and with `Invalid` for a
removed device.

Backing up the user key and restoring it on another device are not requests of this protocol; the key backup
format and the requests that export and import it are specified in a separate document, `KEY-BACKUP.md` (to be
written).

### 6.2 Pairing, enrolment and known users

Pairing exchanges user keys and Rosters between two users (DESIGN.md §5.2); enrolment adds a device to the own
user's Roster. Both are one flow from the client's view, in four modes (§14.10): the side that **shows** a code
and the side that **enters** it, for a user or for a device. Exactly one pairing runs on a Host at a time; a
second `StartPairing` fails with `Busy` until the first ended.

```
StartPairing (272), U:
  mode:         u8              §14.10
  code:         string(512)     modes JoinUser and AddDevice: the code the other side showed; else empty
  OK:   pairing: Pairing

ConfirmPairing (273), U:
  pairing:      u32
  OK:   pairing: Pairing

CancelPairing (274), U:
  pairing:      u32
  OK:   pairing: Pairing

GetPairing (275), U:                    empty
  OK:   pairing: Pairing                the current or the last pairing; NotFound when there was none

ListKnownUsers (276), U S:              empty
  OK:   users: list<User>(1024)         ascending by userId; the own user included

ForgetUser (277), U:
  userId:       id
  OK:   empty
```

The states of a pairing, in order (§14.10): `Waiting` (this side shows `code` and waits for the other side to
connect, or is connecting with the code it was given), `Confirm` (both sides are connected and `sas` is set; the
user compares it with the other side and calls `ConfirmPairing` or `CancelPairing`), `Confirming` (this side
confirmed, the other has not yet), then `Paired`, `Failed` or `Cancelled`. A pairing `expires` 10 minutes after
`StartPairing`, which is a failure. Every change is an event on topic `pairing` (§7.3); the `OK` body shows the
state at the time of the answer. `ConfirmPairing` and `CancelPairing` fail with `NotFound` for a pairing that is
not the current one or has ended; `ConfirmPairing` is `Invalid` in state `Waiting`, and in state `Confirming`
it is `OK` and changes nothing.

Modes `ShowUser` and `JoinUser` need an identity and end, when `Paired`, with the other user known: their key and
current Roster stored, which is a `UserChanged` event. Mode `ShowDevice` requires that no identity is present
(`AlreadyExists` otherwise); the Host creates the device key, if this device has none yet, and shows it in
`code`, and when `Paired` this device holds the user's id, key, current Roster and its own place in it, so
`Identity.present` becomes true with `userKeyPresent` false. A `ShowDevice` pairing that fails or is cancelled
keeps the device key for the next attempt or for `CreateIdentity`. Mode `AddDevice` needs the user key
(`UserKeyAbsent`) and, when `Paired`, has produced a Roster with `seq` one higher that includes the new device,
pushed to every established peer (SPEC.md §7.6).

`ForgetUser` withdraws pairing status: the Host closes every session with the user's devices, deletes the
Roster it holds, and stops listing the user. It is `Invalid` for the own user, for a user who holds a current
Grant from the own user (the app revokes first, §6.4), and for a user who is the owner or a current grantee of
any followed Package the own user does not own, since SPEC.md §1.2 keeps such a user known whatever this
request says; and `NotFound` for a user that is not known. A user known only through a Manifest or a Grant
cannot be forgotten; they stop being known when the Package is unfollowed or the Grant revoked.

### 6.3 Devices

```
ListDevices (288), U S:
  userId:       id              all zero = the own user
  OK:   devices: list<Device>(256)      ascending by deviceId; removed devices included

RemoveDevice (289), U:
  deviceId:     id
  OK:   device: Device
```

`RemoveDevice` produces a Roster with `seq` one higher that marks the device removed, closes its sessions with
`Bye(Unauthorized)` (SPEC.md §8.8) and pushes the Roster to every established peer as an unsolicited `Object`
(SPEC.md §7.6). It needs the user key (`UserKeyAbsent`) and fails
with `Invalid` for this device and with `NotFound` for a device that is not the own user's. `ListDevices` fails
with `NotFound` for a user that is not known.

### 6.4 Packages

```
ListPackages (304), U S:
  after:        optional<id>    the cursor: the last packageId of the previous page; absent = from the start
  kinds:        list<u16>(16)   empty = every kind; ascending
  ownerId:      optional<id>    only Packages of this owner
  followedOnly: bool
  OK:   packages: list<Package>(256)    ascending by packageId
        more:     bool

GetPackage (305), U S:
  packageId:    id
  OK:   package: Package

CreatePackage (306), U:
  kind:         u16
  name:         string(255)     MUST NOT be empty
  retention:    u8              the Manifest's retention: 0 default, 1 everything, 2 latest only
  OK:   package: Package

UpdatePackage (307), U:
  packageId:    id
  name:         optional<string(255)>
  retention:    optional<u8>
  branches:     optional<list<Branch>(64)>     ascending by name, unique
  OK:   package: Package

FollowPackage (308), U:
  packageId:    id
  policy:       u8              §14.14
  OK:   package: Package

UnfollowPackage (309), U:
  packageId:    id
  OK:   package: Package

SetPackagePolicy (310), U:
  packageId:    id
  policy:       u8              §14.14
  OK:   package: Package

ListGrants (311), U S:
  packageId:    id
  OK:   grants: list<Grant>(256)        current Grants, ascending by granteeId

SetGrant (312), U:
  packageId:    id
  granteeId:    id              a known user other than the owner
  rights:       u8              1 read, 2 write, 0 revoke
  OK:   grant: Grant

RevokeGrant (313), U:
  packageId:    id
  granteeId:    id
  OK:   grant: Grant

ListHistory (314), U S:
  packageId:    id
  after:        optional<EntryKey>      the cursor: the last Entry of the previous page; absent = the start
  reverse:      bool            false: ascending total order; true: descending
  kinds:        list<u16>(16)   empty = every kind; ascending
  limit:        u16             1 to 64
  OK:   entries: list<Entry>(64)        in the requested order
        more:    bool

GetEntry (315), U S:
  hash:         hash
  OK:   entry: Entry

GetObject (316), U S:
  hash:         hash
  OK:   object: object

Materialise (317), U S:
  packageId:    id              a followed Package whose Entries reference the object
  hash:         hash            a Tree or a ChunkList
  path:         path            the directory to write the Tree into, or the file to write the ChunkList to
  existing:     u8              1 fail when path exists, 2 replace it, 3 write into it, keeping what is not in the Tree
  OK:   operation: operation            kind Materialise, §8.6

Fetch (318), U S:
  packageId:    id              a followed Package whose Entries reference the object
  hash:         hash
  recursive:    bool            also the objects it references, through Trees and ChunkLists
  pin:          bool            pin what was fetched with reason User (user role) or Service (service role)
  OK:   operation: operation            kind Fetch, §8.6

ListStored (319), U S:
  packageId:    id              all zero = every Package
  kinds:        list<u16>(16)   Entry kinds; empty = every kind in the user role, the registered kinds in the service role
  fromSeq:      u64             the first store sequence to return; 0 reads as 1
  limit:        u16             1 to 64
  OK:   entries: list<Entry>(64)        ascending by storeSeq
        more:    bool
```

Rules:

- `ListPackages` lists every Package the Host knows, Listed and Followed (§5.4), unless `followedOnly`. A page
  is cut so that its frame stays within §3.1; the list maximum of 256 bounds the count, it does not guarantee
  fit.
- `CreatePackage` produces a Manifest with `seq` 1 for a new Package id derived as SPEC.md §2.3 says, owned by
  the own user, followed, with `policy` 0. It needs the user key (`UserKeyAbsent`). Any `kind` is allowed
  (§1.6, decision 16).
- `UpdatePackage` produces a Manifest with `seq` one higher carrying the given fields and the current values of
  the others; an absent field is unchanged. The owner only (`NotOwner`), with the user key (`UserKeyAbsent`).
  `branches` names a head per branch as the owner's declaration (SPEC.md §9.5).
- `FollowPackage` applies to a Package in state Listed (§5.4, decision 20): the Host then wants its Manifest if
  it does not hold it yet, Grants, Heads and history as SPEC.md §10.2 says, and the record moves to Followed
  with a `PackageChanged(Followed)`. A Package the Host knows nothing of is `NotFound`; on a Followed Package
  the request is `OK`, sets `policy` and changes nothing else. `UnfollowPackage` releases the Package's `Head`
  and `Policy` pins; its Entries and objects become evictable and the record returns to Listed while a peer
  lists it, else it is dropped. A Package the own user owns is always followed; `UnfollowPackage` on it is
  `Invalid`.
- `SetGrant` and `RevokeGrant` produce a Grant with `seq` one higher than the current Grant for that grantee, or
  1; `RevokeGrant` is `SetGrant` with `rights` 0 and is `NotFound` for a grantee without a current Grant. The
  owner only (`NotOwner`), with the user key (`UserKeyAbsent`); the grantee MUST be a known user (`NotFound`)
  other than the owner (`Invalid`). Both are user-key requests (§2.2).
- `ListHistory` walks the stored Entries of the Package in the total order of SPEC.md §9.3. A page is cut so that
  its frame stays within §3.1; `limit` and the list maximum of 64 bound the count, they do not guarantee fit.
  `after` names an Entry that need not be stored any more; paging continues after its key.
- `GetEntry` and `GetObject` return only what is stored; a held, evicted or never seen object is `NotFound`.
  `GetObject` returns an object of any tag, Rosters included.
- `Materialise` writes under the rules of SPEC.md §11.4 and §8.4: it fetches what it lacks through the
  Package's peers as `Fetch` does, writes a file only when every chunk is stored, verifies the ChunkList's
  `size`, applies executable mode where the file system supports it, and writes symlinks per device policy.
  The path is opened as the client's user (§13). With `existing` 1 an existing path fails the operation with
  `AlreadyExists`; with 2 the path is deleted first, but only when it is a directory this Host materialised
  earlier (it holds a marker file the Host wrote) or a regular file, and never through a symlink or reparse
  point (`Invalid` otherwise); with 3 files in the Tree replace files of the same name and other files stay.
  Conflicts of SPEC.md §8.4 are counted in the result and named in the operation's `message`. The operation
  fails with `Invalid` when `hash` turns out not to be a Tree or a ChunkList, with `IoError` when the file
  system refuses, and with `NotFound` as `Fetch` does.
- `Fetch` wants the object and, when `recursive`, everything it references, from peers that may serve it
  (SPEC.md §10.3, §10.4); objects already stored are counted as done at once and, with `pin`, pinned too. The
  operation fails with `NotFound` when, for 60 seconds, no established peer is a possible source for an
  object it still lacks (every source answered `NotFound`, or none is connected); `message` names the hash.
  Until then it waits, with `message` saying for what.
- `ListStored` walks Entries across every Package, or one, in store-sequence order; it exists so that an
  indexer catches up after it was away (§9.3), and so that a lapsed subscription (§7.4) is refilled.

### 6.5 Pins and the store

```
ListPins (352), U S:
  hash:         optional<hash>  only pins of this object
  packageId:    optional<id>    only pins whose source is this Package
  after:        optional<PinKey>        the cursor; absent = the start
  OK:   pins: list<Pin>(256)            ascending by PinKey
        more: bool

Pin (353), U S:
  hash:         hash
  OK:   pin: Pin

Unpin (354), U S:
  hashes:       list<hash>(1024)        ascending as byte strings
  OK:   removed: u16            pins that existed and were removed

GetStore (355), U S:                    empty
  OK:   store: Store

SetStoreBudget (356), U:
  bytes:        u64             0 = the device default
  OK:   store: Store

ReleasePins (357), U:
  reason:       u8              §14.8: User or Service
  source:       id              the pins' source (§5.11); for Service, H(service name)
  OK:   removed: u32            pins removed
```

`Pin` in the user role creates a pin with reason `User` and source all zero; in the service role, reason
`Service` and the service's source (§5.11); it is idempotent. `Unpin` removes that role's pin on each object
and counts what it removed; it never fails for a pin that is not there, and pins of other reasons and
sources are not touched. `Pin` on an object that is not stored is `NotFound`. `ReleasePins` is the user's way
to clean up after a service that is gone (§9.6): it removes every pin with that reason and source, and is
`Invalid` for reasons `Head`, `Policy` and `Outbound`, which the Host alone holds. A budget change may start
eviction (SPEC.md §11.3) at once; `GetStore` shows the result over time, and topic `store` pushes it. A request
that would store a new object (`PutObject`, `PutFile`, `WriteEntry`, `Fetch`, `Materialise`) fails with
`StoreFull` when the bytes of pinned objects plus the new object would exceed the budget; eviction cannot
help, because pinned objects are not evicted.

### 6.6 Peers

```
ListPeers (368), U S:                   empty
  OK:   peers: list<Peer>(1024)         known peers only, ascending by deviceId

ListSessions (369), U S:                empty
  OK:   sessions: list<Session>(256)    ascending by deviceId

ListDiscovered (370), U S:              empty
  OK:   peers: list<Peer>(1024)         every device discovery saw in the last hour, known or not, ascending by deviceId

ListStaticPeers (371), U S:             empty
  OK:   peers: list<StaticPeer>(256)    ascending by address as byte strings

AddStaticPeer (372), U:
  address:      string(256)     host:port
  OK:   peer: StaticPeer

RemoveStaticPeer (373), U:
  address:      string(256)
  OK:   empty

Dial (374), U:
  deviceId:     id              all zero = whatever device answers at the address
  address:      string(256)     empty = the addresses discovery knows for the device
  OK:   empty
```

`AddStaticPeer` fails with `AlreadyExists`, and with `Busy` when 256 static peers are configured;
`RemoveStaticPeer` fails with `NotFound`; a syntactically wrong address is `Invalid`. `Dial` with both fields
empty is `Invalid`; with a device that is not known it is `NotFound`;
with a device that has an established session it is `AlreadyExists`. `OK` means the dial was started under
SPEC.md §4.4, backoff included; its outcome is an event on topic `peers`.

### 6.7 Services

```
ListServices (384), U S:                empty
  OK:   services: list<Service>(64)     every configured service, ascending by name

ServiceCall (385), U:
  service:      name
  method:       name
  payload:      payload
  OK:   payload: payload
```

`ServiceCall` is forwarded to the named service as a `ForwardedCall` callback (§9.8); the service's reply body
is returned as the OK body, and a non-zero reply status is returned as the response's status with the
service's `Error` record. The meaning of `method` and both payloads is the service's own, described in that
service's document. Errors: `ServiceUnavailable` when the service is not configured or not connected, or when
its connection ends before it replied; `ServiceTimeout` when it did not reply within 60 seconds; `Busy` when 64
callbacks are outstanding toward it.

## 7. Subscriptions and events

### 7.1 `Subscribe` (400) and `Unsubscribe` (401)

```
Subscribe (400), U S:
  subscription: subscription    chosen by the client
  topic:        u16             §14.5
  filter:       Filter          §5.18
  OK:   empty

Unsubscribe (401), U S:
  subscription: subscription
  OK:   empty
```

A `subscription` id MUST differ from every live subscription of the connection (`AlreadyExists`); it is free
again once the `Unsubscribe` response or the `SubscriptionLapsed` event (§7.4) was sent. A connection holds at
most 64 subscriptions (`Busy`). An unknown topic or a filter field the topic does not read is `Invalid`;
`Unsubscribe` of an id that is not a live subscription is `NotFound`. The Host MUST send the `Response` to
`Subscribe` before the first event of the subscription, and MUST NOT send an event of a subscription after the
`Response` to its `Unsubscribe`. Events reflect changes that happen after the `Response` to `Subscribe`, except
where a topic says it replays; a client therefore subscribes before it lists (§3.4).

### 7.2 Topics

| Topic | Id | Filter fields read | Event | Delivered when |
|---|---|---|---|---|
| `packages` | 1 | `packageId`, `kinds` (Package kinds) | `PackageChanged` | a Package is listed by a peer for the first time, created, followed, unfollowed, gets a new current Manifest or Grant, a new or changed Head, or a new local policy; in the order the changes happened |
| `entries` | 2 | `packageId`, `kinds` (Entry kinds), `fromSeq` | `EntryStored` | an Entry is stored; in store-sequence order, each storing once (an Entry stored again after eviction appears again, §5.6). With `fromSeq` > 0 the Host first sends every stored Entry matching the filter with `storeSeq` ≥ `fromSeq`, in order, then continues live without a gap or a repeat; when more than 256 stored Entries match, `Subscribe` fails with `GapTooLarge` and the client pages with `ListStored` first |
| `peers` | 3 | `deviceId` | `PeerChanged` | a device is discovered, dialled, establishes a session, loses one, refuses or is refused, or is forgotten; in the order it happened |
| `transfers` | 4 | `packageId`, `deviceId` (the peer) | `TransferChanged` | an object is wanted, received, sent, not found, cancelled or dropped; in the order it happened |
| `operations` | 5 | `operation` | `OperationChanged` | an operation of this connection starts, progresses (at most once per second per operation), or ends; every state change is delivered, a progress change may be coalesced; the first event of an operation follows the `Response` that started it (§3.4) |
| `pairing` | 6 | none | `PairingChanged` | the current pairing changes state or a field |
| `telemetry` | 7 | `series`, `key` | `TelemetrySample` | a minute closes, for every `(series, key)` that changed within it, and (client capability bit 0) partial samples, §10.4; ascending by minute, then by series and key as byte strings |
| `service` | 8 | `service`, `packageId` | `ServiceEvent` | a service calls `Emit` (§9.10); in the order of the calls |
| `users` | 9 | `userId` | `UserChanged` | a user becomes known, a new current Roster is held for a known user (the own user included), or a user is forgotten |
| `store` | 10 | none | `StoreChanged` | the store's counts or budget changed; at most once per second |

Topics 11 to 255 are reserved for extensions. A subscription with `packageId`, `deviceId` or `userId` all zero
receives the events of every Package, device or user.

### 7.3 Events

```
PackageChanged (1024):
  change:       u8              §14.12
  package:      Package         after the change
  grant:        optional<Grant> present for change Grant
  head:         optional<Head>  present for change Head

EntryStored (1025):
  entry:        Entry

PeerChanged (1026):
  change:       u8              §14.11, peer change kinds
  peer:         Peer
  session:      optional<Session>       present for Established and Closed

TransferChanged (1027):
  transfer:     Transfer

OperationChanged (1028):
  operation:    Operation

PairingChanged (1029):
  pairing:      Pairing

TelemetrySample (1030):
  series:       name
  key:          string(192)
  minute:       time
  value:        u64
  partial:      bool            true for a sample of a minute that has not closed

ServiceEvent (1031):
  service:      name
  event:        name
  packageId:    id              all zero when the event concerns no Package
  payload:      payload

UserChanged (1032):
  change:       u8              1 known, 2 roster, 3 forgotten
  user:         User
  devices:      list<Device>(256)       the current Roster's devices, ascending by deviceId; empty for 3

StoreChanged (1033):
  store:        Store

SubscriptionLapsed (1034):
  dropped:      u32             events discarded
  lastSeq:      u64             topic entries: the storeSeq of the last EntryStored handed to the pipe; else 0
```

### 7.4 Queues and the slow-consumer rule

The Host queues the events of each subscription separately and writes them to the connection as the pipe
accepts bytes; it MUST NOT block its own work on a client. A subscription's queue holds at most 256 events and
at most 16 MiB of encoded events, whichever bound is reached first. When an event would break either bound,
the Host discards the queue, ends the subscription, and queues in its place one `SubscriptionLapsed` naming
how many events were discarded (the queued ones plus the one that did not fit) and, for topic `entries`, the
store sequence of the last `EntryStored` that was handed to the pipe, so that the client re-subscribes with
`fromSeq` one higher. A lapse ends only that subscription; the connection, its other subscriptions and its
requests are unaffected.

A replay (`fromSeq`) is generated lazily: the Host reads the next stored Entry from the store as the queue
drains and never generates more than 256 events for a replay, so a replay that fits never lapses on its own;
live events that arrive during a replay are queued behind it and count against the same bounds. The 16 MiB
bound counts events that are queued but not yet written. Separately, the Host MUST NOT hold more than 64 MiB
of encoded frames of one connection, responses included, handed to the pipe and not yet accepted by it, nor
more than 256 MiB across every connection; when it would, or when a frame could not be written within 30
seconds, it closes the connection with `Bye(Unresponsive)`, choosing the connection with the most waiting bytes
for the global bound.

## 8. Operations

### 8.1 Model

An operation is work the Host does for one connection that may take longer than a response should: writing a
tree to disk, fetching objects from peers, chunking a file. A request that starts one (`Materialise`, `Fetch`,
`PutFile`) is answered at once with the Host-chosen `operation` id, unique for the connection's lifetime, and
the `Operation` record (§5.13) then changes until it ends. An operation belongs to the connection that started
it: no other connection sees it, and it is cancelled when the connection ends. A connection has at most 16
operations running at once (`Busy`); the Host keeps the records of the last 48 ended operations of a connection
and drops the oldest beyond that, so that `ListOperations` (§8.5) always fits its list.

### 8.2 States

```
          progress            Done
Running ───────────► Running ───────► ended
   │                              Failed
   ├──────────────────────────────► ended
   │              Cancelled
   └──────────────────────────────► ended
```

| State | Value | Meaning |
|---|---|---|
| Running | 1 | work is in progress; `done`, `total` and `message` change |
| Done | 2 | ended; `result` holds the kind's result body |
| Failed | 3 | ended; `status` holds the reason, `message` the explanation |
| Cancelled | 4 | ended by `CancelOperation`, `Drain` or the connection's end; `status` is `Cancelled`; what was done stays done |

An ended operation never changes again.

### 8.3 Progress

While Running the Host updates `done` and, when it learns it, `total`, and emits `OperationChanged` on
subscriptions of topic `operations` at most once per second per operation; the final event of an operation is
always emitted. A client without a subscription polls with `GetOperation`.

### 8.4 Completion

An operation ends with `state` Done, Failed or Cancelled and exactly one final `OperationChanged`. Done carries
the result body of its kind (§8.6) in `result`; Failed carries a `status` from the kind's row in §8.6;
Cancelled carries `status` `Cancelled`.

### 8.5 Cancel

```
ListOperations (416), U S:              empty
  OK:   operations: list<Operation>(64) this connection's, ascending by operation id

GetOperation (417), U S:
  operation:    operation
  OK:   operation: Operation

CancelOperation (418), U S:
  operation:    operation
  OK:   operation: Operation            the record at the time of the answer; Cancelled once the Host stopped
```

`CancelOperation` on an ended operation is `OK` and changes nothing; on an unknown one it is `NotFound`. A
cancelled `Materialise` leaves the files it wrote; a cancelled `Fetch` cancels its outstanding wants (SPEC.md
§7.8) and keeps what was stored; a cancelled `PutFile` keeps the chunks already stored, pinned as §9.5 says, and
no ChunkList.

### 8.6 Kinds and results

| Kind | Value | Started by | `unit` | Result body | Fails with |
|---|---|---|---|---|---|
| Materialise | 1 | `Materialise` (§6.4) | 1 bytes | `files: u32  bytes: u64  conflicts: u32` | `AlreadyExists`, `Invalid`, `IoError`, `NotFound`, `StoreFull` |
| Fetch | 2 | `Fetch` (§6.4) | 2 objects | `objects: u32  bytes: u64` | `NotFound`, `StoreFull` |
| PutFile | 3 | `PutFile` (§9.5) | 1 bytes | `hash: hash  size: u64  chunks: u32` (the ChunkList) | `NotFound` (no such path), `Invalid` (not a regular file), `IoError`, `StoreFull` |

Values 4 to 255 are reserved for extensions. `unit` 3 is reserved.

## 9. Service role

### 9.1 What a service is

A service is a client in the service role that defines a Package kind and the Entry kinds in it: Sync defines
`Archive` with `Version`; Chat defines `Channel` with `Message` and `Redaction` (SPEC.md §14.7, §14.8). It is
written against this protocol only (DESIGN.md §10.3). In the first release Sync and Chat run inside the Host
process, connected to the local port over an in-memory stream pair; the contract below MUST NOT depend on that,
and a service that runs as its own program on the pipe sees exactly the same messages. A service may send
every request of §6 marked **S**, and the requests of this section.

A service that connects does, in this order: `Hello` with role 2, its name and, when the Host spawned it, its
token; `RegisterKind` for each of its Package kinds; a comparison of `HelloAck.storeEpoch` with the epoch its
own index was built against (§9.3); `Subscribe` to `entries` for its Entry kinds with `fromSeq` set to one past
the last store sequence it has indexed (after `ListStored` when the gap is large), and to `packages` for its
Package kinds. From then on it receives everything it needs as events.

### 9.2 `RegisterKind` (512)

```
RegisterKind (512), S:
  kind:         u16             a Package kind, SPEC.md §14.8
  entryKinds:   list<u16>(16)   the Entry kinds of this Package kind, ascending, non-empty
  OK:   storeEpoch: id          the store epoch (§9.3)
        storeSeq:   u64         the store sequence of the newest stored Entry; 0 when none
        packages:   u32         followed Packages of this kind
```

A service may register only the Package kinds and Entry kinds its configuration entry lists (§4.2); any other
is `NotPermitted`. A kind is held by at most one connected service; a kind another connected service
registered is `KindTaken`, and an Entry kind another registered kind lists is `Invalid`. Registering a kind
this service already holds is `OK` and changes nothing. A registration lasts while the connection does and is
released when it ends. `WriteEntry` and `Emit` are scoped to the registered kinds (`KindNotRegistered`).
`Service.kinds` and `Service.entryKinds` (§5.12) show the configured set whether or not the service is
connected.

### 9.3 The streams a service receives

Through topic `entries` with its Entry kinds in the filter, a service receives every Entry of its kinds the
Host stores, from any peer and from its own `WriteEntry` alike, once per storing, in store-sequence order, with
the replay rule of §7.2 for catch-up. Through topic `packages` with its Package kinds in the filter it receives
every listing, follow, unfollow, Manifest, Grant, Head and policy change of Packages of its kinds. The Host
MUST deliver these on the same subscription machinery as for a user client, with the queue bounds and the
lapse rule of §7.4; a service that lapses re-subscribes with `fromSeq` from `SubscriptionLapsed.lastSeq` plus
one, paging with `ListStored` when the gap exceeds 256.

Store sequences are meaningful only within one **store epoch** (§1.2). A service or any other client that
keeps an index of its own MUST record the epoch it indexed under and, when `HelloAck.storeEpoch` or
`RegisterKind`'s `storeEpoch` differs, discard that index and catch up from store sequence 1. The Host changes
the epoch whenever its database is restored from a backup, reset, or created anew, and never otherwise.

### 9.4 `WriteEntry` (513)

```
WriteEntry (513), S:
  packageId:    id              a followed Package of a kind this service registered
  kind:         u16             an Entry kind this service registered for that Package kind
  parents:      list<hash>(2)   ascending as byte strings; empty = the Host chooses
  payload:      payload
  references:   list<hash>(1024)  ascending; extension kinds only: the objects the payload references (see below)
  OK:   entry: Entry
```

The Host builds the Entry object of SPEC.md §8.6 as follows and then signs it with the device key under domain
`axiom-mesh/entry/1`:

1. `packageId`, `kind` and `payload` as given; `deviceId` the own device.
2. `parents`: as given, when given; the Host checks that each is a stored Entry of the same `packageId`
   (`Invalid` otherwise). When empty, the rule of SPEC.md §9.3: the own device's Head of the Package, if it has
   one; else, when the Host has stored any Entry of the Package, the stored Entry highest in the total order;
   else none. When the own Head is the parent and the clock of step 3 would exceed the Head's clock by more
   than 2^32 (SPEC.md §8.6), the Host names two parents, the own Head and the stored Entry highest in the total
   order, as SPEC.md §9.3 allows. A service writing a `Version` to an Archive with more than one leaf MUST name
   the leaves it merges itself (SPEC.md §9.5); the Host does not know branches.
3. `clock`: one more than the highest `clock` of any Entry the Host has stored for the Package, or 0 when it
   has none (SPEC.md §9.3). When that value would not be well-formed under SPEC.md §8.6, or the parents of
   step 2 cannot make it faultless, the request fails with `ClockExhausted` and the Host surfaces it to the
   user.

Before signing the Host checks that the Package is followed (`NotFound`), that its kind and the Entry kind are
registered by this service (`KindNotRegistered`), that the own user may write (owner or current `Write`
grantee, and this device not removed; `NotWritable`), and that the result is well-formed and would be valid
under SPEC.md §8.6 and faultless under §8.6 and §9.3 once its parents are considered; a result that would not
be is `Invalid`.

**Payloads and references.** The Host parses the payload of kinds 1 to 3 itself, as the layouts of SPEC.md
§8.5 say (decision 5): a payload that does not parse exactly to its end is `Invalid`, and the objects it
references (a `Version`'s Tree, a `Message`'s attachments) are taken from the parse; for these kinds
`references` MUST be empty (`Invalid` otherwise). For every other kind the Host does not parse the payload and
takes `references` as the writer's statement of what the payload names; it may be empty. In both cases every
referenced object MUST already be stored (`PutObject`, `PutFile`; `NotFound` names the first that is not),
because the Host does not fetch what a local writer referenced, and the Host then

1. stores the Entry, assigns its store sequence and updates its Head;
2. pins the Entry with reason `Head`;
3. pins every referenced object, and through Trees and ChunkLists everything they reference, with reason
   `Outbound` and source = the Package id, and releases that pin per object once the object has been sent to
   at least one peer (SPEC.md §11.2); on a device whose policy would evict the object this is the only thing
   that keeps it until a peer has it;
4. pins the referenced objects the Package's policy asks for with reason `Policy`;
5. replicates (`PackageChanged` and `Have`, SPEC.md §10.5).

The `OK` body is the stored Entry with its hash and store sequence; the same Entry arrives on the service's
`entries` subscription. A `Redaction` (kind 3) the Host stores, from this request or from a peer, sets
`Entry.redacted` on its target under the rules of SPEC.md §9.4. `WriteEntry` is not idempotent (§2.3).

### 9.5 `PutObject` (514) and `PutFile` (515)

```
PutObject (514), S:
  object:       object
  OK:   hash:   hash
        stored: bool            false when the object was already stored

PutFile (515), S:
  path:         path            a regular file the Host can read
  OK:   operation: operation    kind PutFile, §8.6
```

`PutObject` names the object by `H(object)`, checks it as SPEC.md §8.1 to §8.4 say for its tag (an unknown tag is
opaque and accepted; a malformed object is `Invalid`), stores it atomically (SPEC.md §11.1), and pins it with
reason `Service` for this service. A signed object (tags 4 to 7) MUST NOT be put: that is `Invalid`; Entries are
written with `WriteEntry` and the rest the Host produces itself.

`PutFile` reads the file at `path` as the client's user (§13), cuts it into Chunks of exactly 1 MiB except the
last (SPEC.md §8.2), stores each Chunk and the ChunkList (SPEC.md §8.3), pins every one of them with reason
`Service` for this service, and ends with the ChunkList's hash in the result. A file that changes while it is
read fails the operation with `IoError`. A zero-length file produces a ChunkList with no chunks. Both requests
fail with `StoreFull` (§6.5) when the pinned bytes would exceed the budget.

The RECOMMENDED pattern for a service is: put the objects, write the Entry that references them, then `Unpin`
them; from then on the `Outbound` pin holds each object until a peer has fetched it, the Package's `Policy`
pins hold what the policy wants, and the rest is evictable, which is what the user's retention choice means.

### 9.6 Pins with reason `Service`

A service pins with `Pin` and `Unpin` (§6.5); its pins have reason `Service` (§14.8) and the source of §5.11,
and are independent of every other reason. The Host releases every pin of a service when the service's
connection ends only if the Host's configuration says so for that service; by default they stay, because an
out-of-process service may restart. The user releases what a service left behind with `ReleasePins` (§6.5).
`Fetch` with `pin` true pins with reason `Service` when called by a service.

### 9.7 `GetStorePath` (516) and direct reads

```
GetStorePath (516), S:                  empty
  OK:   path:   path            the store directory
        layout: u8              = 1
```

A service MAY read stored objects directly from the store directory, read-only, instead of through
`GetObject`. In layout 1 the object with hash `h` (64 hex characters `h[0..63]`) is the file

```
<path>/objects/<h[0..1]>/<h[2..3]>/<h>
```

holding exactly the object's bytes. A file there is complete and verified (SPEC.md §11.1: written to a
temporary name and renamed into place). A reader MUST open the file by name and treat its absence as "not
stored" (never stored, or evicted since), falling back to `GetObject` or `Fetch`; it MUST open it so that the
Host can delete it meanwhile (on Windows with `FILE_SHARE_DELETE`; a reader that holds a file open keeps its
bytes, not its name); it MUST NOT write, rename or delete anything in the directory, and MUST NOT list the
directory to learn what exists (`ListPins`, `GetEntry` and the records say what exists). The Host treats a
delete that fails because a reader holds the file as "evict later" and does not count the object as evicted
until the unlink succeeded. The Host fails `GetStorePath` with `NotPermitted` when the directory is not
readable by this connection's user (Host capability bit 0 clear in its `HelloAck`, §14.4).

### 9.8 Callbacks

```
ForwardedCall (1537):
  client:       u32             the Host's number for the calling connection, for logs
  clientName:   name            the caller's Hello.name
  method:       name
  payload:      payload
  Reply OK:     payload: payload
  Reply status: any code of §14.9 or a service-defined code (§11.2), returned to the caller as its status
```

**`ForwardedCall`.** A `ServiceCall` (§6.7) from a user client, forwarded verbatim. The service MUST reply
within 60 seconds, the callback's deadline (§3.6); the Host answers the caller `ServiceTimeout` otherwise and
ignores a later reply. The service answers with its own payload, or with a status, which the Host passes to
the caller unchanged, `Error` record included. When the caller's connection ended before the reply, the reply
is read and discarded.

**No payload callback.** The Host does not ask a service about payloads it receives from peers: it parses the
layouts of kinds 1 to 3 itself (decision 5, §9.4) and stores an Entry of any other kind opaquely, exactly as
SPEC.md §8.5 and §9.2 say. A service that finds an Entry wrong by its own rules shows or hides it in its
namespace; it cannot keep the Host from storing or forwarding it. The id 1536 of draft 1's `ValidatePayload`
is reserved (§14.1).

When 64 callbacks are outstanding toward a service, the Host answers further `ServiceCall`s with `Busy`.

### 9.9 `AddTelemetry` (517)

```
AddTelemetry (517), S:
  series:       name            MUST start with "svc." followed by this service's name and "."
  key:          string(192)
  delta:        u64
  OK:   empty
```

Adds `delta` to the counter `(series, key)` for the current minute and to its lifetime total (§10). A series
outside the service's prefix is `NotPermitted`; a service name is at most 32 bytes (§3.2) so that the prefix
and a name of the service's own fit a `name`. Services have counters only; gauges are the Host's. On a Host
without telemetry (Host capability bit 2 clear, §14.4) the request is `NotFound`.

### 9.10 `Emit` (518)

```
Emit (518), S:
  event:        name            the service's own event name
  packageId:    id              the Package it concerns; all zero when none
  payload:      payload
  OK:   empty
```

Publishes a `ServiceEvent` to every subscription of topic `service` whose filter matches this service and the
Package. This is how a service's namespace pushes (a timeline changed, a folder's sync state moved) without a
user client polling `ServiceCall`. A Package the Host does not know or does not follow is `NotFound`; a
followed Package of a kind this service did not register is `KindNotRegistered`; all zero is always allowed.

## 10. Telemetry

### 10.1 Series and keys

A telemetry **series** is a named measurement with a kind, counter or gauge; a **key** names what is measured
within the series (a peer, a Package, a peer and a Package, a port, nothing). The series the Host keeps are
listed in §14.13 with their key; a service adds counters under its own prefix (§9.9). A key is at most 192
bytes so that a peer device id and a Package id in hex, joined by `:`, fit. The series `local.requests` and
`local.violations`, keyed by a name the client chooses, hold at most 256 distinct keys each; a further name is
counted under the key `other`.

### 10.2 Resolution and retention

The Host keeps, per `(series, key)`, one value per minute: for a counter the sum of what was added within the
minute, for a gauge the last value set within it. Minutes with nothing added or set have no point. A point is
kept for 7 days (10 080 minutes) after its minute and then dropped; counters are also summed into a lifetime
total that is never dropped, and a gauge keeps its last value and the minute it was set. A minute closes when
the Host's clock passes its end; points of the current minute are partial until then. Nothing leaves the device.

### 10.3 Requests

```
ListSeries (432), U S:                  empty
  OK:   series: list<TelemetrySeries>(256)      ascending by name

ListKeys (433), U S:
  series:       name
  after:        optional<string(192)>   the cursor; absent = the start
  OK:   keys: list<string(192)>(1024)   ascending as byte strings; keys with any point or total
        more: bool

QueryTelemetry (434), U S:
  series:       name
  key:          string(192)
  from:         time            inclusive; rounded down to the minute
  to:           time            exclusive; rounded down to the minute
  OK:   points: list<TelemetryPoint>(10080)     ascending by minute; minutes without a point omitted

GetTotal (435), U S:
  series:       name
  key:          string(192)
  OK:   value:  u64             counter: the lifetime sum; gauge: the last value
        since:  time            counter: when counting began; gauge: the minute of the last value
```

An unknown series is `NotFound`; a range of more than 7 days, or with `to` not after `from`, is `Invalid`; an
unknown key is `OK` with no points and a total of 0. On a Host without telemetry (Host capability bit 2 clear)
every request of this section, `AddTelemetry` (§9.9) and a `Subscribe` to topic `telemetry` are `NotFound`.

### 10.4 Live

Topic `telemetry` (§7.2) delivers a `TelemetrySample` for every `(series, key)` that got a point in the minute
that just closed, with `partial` false, in ascending order of series and key, within 5 seconds of the minute's
end. A client whose `Hello` set capability bit 0 also receives samples with `partial` true for the current
minute, at most once per second per `(series, key)` and only when the value changed. The app renders history
from `QueryTelemetry` and the present from the same subscription.

## 11. Errors

### 11.1 Status codes

The `status` of a `Response` or `Reply`:

| Code | Name | Meaning |
|---|---|---|
| 0 | OK | the OK body follows |
| 1 | UnknownRequest | the message id is in the request or callback range and unknown to the receiver |
| 2 | NotPermitted | the connection's role may not send this request, or the request reaches outside what the role may touch |
| 3 | NotFound | the named user, device, Package, Entry, object, pin, peer, pairing, operation, subscription, series, address or path does not exist here, or no peer can supply an object |
| 4 | AlreadyExists | an identity (`CreateIdentity`, `ShowDevice`), static peer, subscription id, session or path already exists |
| 5 | Invalid | a value is in bounds but wrong in substance; `Error.detail` names the field |
| 6 | NoIdentity | the request needs an identity and the device has none |
| 7 | UserKeyAbsent | the request needs the user's private key, which is on another device |
| 8 | NotOwner | the request needs the Package's owner |
| 9 | NotWritable | the own user has no `Write` right on the Package, or this device is removed |
| 10 | KindTaken | another connected service holds the kind |
| 11 | KindNotRegistered | the request needs a kind this service did not register |
| 12 | ServiceUnavailable | the service is not configured or not connected |
| 13 | ServiceTimeout | the service did not reply in time |
| 14 | Busy | a limit of §12 is reached: outstanding callbacks, subscriptions, operations, static peers, one pairing |
| 15 | Draining | the session is Draining |
| 16 | Cancelled | `Operation.status` of an operation in state Cancelled (§8.2); never the status of a `Response` |
| 17 | IoError | the file system refused: `Error.detail` carries the platform message |
| 18 | ClockExhausted | no parents make the next clock well-formed and faultless (SPEC.md §8.6, §9.3); the Package is read-only on this device |
| 19 | GapTooLarge | the replay would exceed 256 Entries; page with `ListStored` first |
| 20 | StoreFull | storing the object would put the pinned bytes over the budget (§6.5); nothing evictable can make room |
| 21 to 32767 | (reserved) | for extensions of this protocol |
| 32768 to 65535 | (service-defined) | §11.2 |

### 11.2 Service-defined codes

A `Reply` to `ForwardedCall` MAY carry a status from 32 768 to 65 535, whose meaning the service's own document
states; the Host returns it to the caller unchanged with the service's `Error` record. A service MUST NOT use a
code below 32 768 in a `ForwardedCall` reply except those of §11.1 with their meaning here.

### 11.3 Violations

A violation is any of: a frame outside its bounds or not completed in time (§3.1); a message id in the session
range and unknown, in the reserved range 34 to 255, or 2048 and above; a frame of a class the sender may not
send (§3.3); a message in the wrong state (§4.1), a first frame other than `Hello` or `Bye` and a second
`Hello` included; a field outside its bounds as §3.2 defines them (length, count, width, `bool`, `optional`,
UTF-8) in a request or reply; a `correlation` of 0, one that is still outstanding, or a 65th outstanding
request (§3.4); a `Reply` for a callback id the Host never issued on this connection (§3.6); `Hello.role`
outside the registry. On a violation the Host MUST close the connection with `Bye(Violation)` and SHOULD log
the connecting process. Everything else that is wrong with a request, a wrong value in substance (§3.2)
included, is an error response, and the connection continues. A missing `Hello` (§2.3) is not a violation:
the connection is closed without a frame.

A Host defect seen by a client (a `Response`, `Event` or `Callback` outside its bounds, a `Response` for no
outstanding request, an event on no live subscription) is handled by the client as a lost connection: it
closes and reconnects (§2.3). An enumerated value the client does not know is not a defect (§1.5).

## 12. Limits

Every limit is a bound the Host MUST enforce and a client MUST respect.

| Limit | Value | On breach |
|---|---|---|
| frame length | 2 to 4 198 400 bytes | violation |
| frame completion | 30 s from the first byte | violation |
| `Hello` after connecting | 10 s | close without a frame |
| connections waiting for `Hello` | 16 | the longest-waiting closed |
| `Pong` after `Ping` | 30 s; `Ping` after 60 s without receiving | `Bye(Unresponsive)` |
| outstanding requests per connection | 64 | violation |
| outstanding callbacks per connection | 64 | `Busy` (§9.8) |
| subscriptions per connection | 64 | `Busy` |
| queued events per subscription | 256 events or 16 MiB | lapse (§7.4) |
| replay per `Subscribe` | 256 Entries | `GapTooLarge` |
| frames handed to the pipe, not yet accepted | 64 MiB per connection, 256 MiB per Host, or 30 s for one frame | `Bye(Unresponsive)` |
| running operations per connection | 16 | `Busy` |
| ended operations kept per connection | 48 | oldest dropped |
| user connections | 64 | `Bye(Refused, "busy")` |
| service connections | 1 per configured name | `Bye(Duplicate)` |
| pairings per Host | 1 at a time, 10 minutes | `Busy` / `Failed` |
| static peers | 256 | `Busy` |
| `Drain` grace | 5 000 ms default, 30 000 ms at most | `Bye(Shutdown)` |
| `ServiceCall` / `ForwardedCall` | 60 s | `ServiceTimeout` |
| `Fetch` without a source | 60 s | `NotFound` (§6.4) |
| pinned bytes | the store budget | `StoreFull` (§6.5) |
| page of `ListHistory`, `ListStored` | 64 Entries, cut to the frame | — |
| page of `ListPackages`, `ListPins` | 256, cut to the frame | — |
| lists without a cursor | their maximum, cut to the frame (§6) | — |
| points per `QueryTelemetry` | 10 080 (7 days) | `Invalid` |
| telemetry retention | 7 days of minutes; totals for ever | dropped |

## 13. Security considerations

- **Authentication is the operating system's.** The pipe admits the OS user the Host serves and nobody else,
  by its access control list and, behind it, by the process identity the Host reads for every connection
  (§2.2); a Host that runs as a system service admits the users its allow list names. A process of the same
  OS user is trusted as that user, as with ssh-agent and Docker Desktop: that user may do everything the user
  role may do anyway, on the same machine; the protocol adds no finer right, and claims none. The form of the
  access list and of the allow list is `PIPE-ACCESS.md` (§2.4, to be written).
- **The user-key requests are logged and may be confined.** The requests that produce a signed Roster or Grant
  on the user's behalf (§2.2) are the ones a local program could not forge without the pipe, because the user
  key never leaves the Host. Every one is logged with the process id and executable of its connection, and
  the configuration MAY confine them to listed executables, so that a stray script does not enrol a device or
  grant a Package. That is a fence against accident and a tripwire against intent, not a sandbox.
- **The service role is configuration plus provenance.** A program becomes a service by claiming a name the
  Host's configuration lists (§4.2), and the configuration binds the name to the kinds it may register (§9.2).
  A name configured as in-process is never granted on the pipe, and a name the Host spawns is granted only
  with the token the Host put into that child's environment; a program of the same user that neither is nor
  was spawned cannot claim either. The role exists so that two services do not hold one kind at once and so
  that a stray program does not register a kind by accident; against a hostile program of the same user it is
  the process identity of §2.2 that stands, and the rest is the threat model of DESIGN.md §14.
- **No key crosses the pipe.** Signing happens in the Host; a service hands over bytes and gets back a signed
  Entry. The user key's backup is the one exception and is specified apart (§6.1). A `Response` never carries a
  private key, and `GetObject` of a Roster returns public keys only. The service token (§4.2) is a proof of
  spawning, not a key: it opens nothing but the service role for one child.
- **Paths are the client's.** `Materialise`, `PutFile` and `GetStorePath` name paths the Host opens as the user
  of the connection that named them (Windows: `ImpersonateNamedPipeClient` around every open, create, delete
  and rename; on Unix the Host runs as that user); a Host under another account therefore reads and writes
  exactly what the client's user may. `Materialise` with `existing` 2 deletes only a directory this Host
  materialised or a regular file, and never through a symlink or reparse point (§6.4). The Host MUST NOT
  follow a symlink inside a path it materialises into out of the target directory (SPEC.md §8.4's escape rule
  applies to the destination as a whole). Host capability bits 0 and 1 (§14.4) are computed per connection
  from the same identity.
- **Everything a client sees is verified.** Records are derived from objects the Host verified under SPEC.md;
  `GetObject` returns stored objects only; direct reads see files that are complete and verified. A client is
  never handed a held object.
- **Payloads are parsed by the Host, not judged by a service** (§9.4, §9.8). The Host checks the built-in
  layouts itself and stores everything SPEC.md calls storable; no service can keep this device from storing or
  forwarding a peer's Entry, and no Entry waits in the held buffer for a program that may not be running. What
  a service thinks of an Entry's content shows in its namespace only.
- **Bounded by construction.** Frames, lists, outstanding requests and callbacks, subscriptions, queues (in
  events and in bytes), operations, connections before and after `Hello`, static peers and pinned bytes are
  bounded (§12), and the Host never blocks on a client (§7.4). A client that stops reading loses its
  connection, not the mesh; a service that is gone leaves pins the user can release (§6.5).
- **Telemetry stays on the device** (§10.2). It names peers, Packages and amounts; it is readable by every local
  program of the user, like everything else over the pipe.
- **The pipe cannot be squatted or reached from the network** (§2.1): first-instance and reject-remote flags on
  Windows, a socket file taken over only after it refused a connection on Unix, and a client that checks who
  serves the pipe where it can. `AXIOM_MESH_PIPE` is as trusted as the environment it came from.

## 14. Registries

### 14.1 Message ids

| Id | Message | Class | § |
|---|---|---|---|
| 1 | Hello | Session | 4.2 |
| 2 | HelloAck | Session | 4.2 |
| 3 | Bye | Session | 4.3 |
| 4 | Ping | Session | 4.4 |
| 5 | Pong | Session | 4.4 |
| 6 | Drain | Session | 4.5 |
| 32 | Response | Response | 3.4 |
| 33 | Reply | Reply | 3.6 |
| 256 | GetIdentity | Request | 6.1 |
| 257 | CreateIdentity | Request | 6.1 |
| 258 | RenameDevice | Request | 6.1 |
| 272 | StartPairing | Request | 6.2 |
| 273 | ConfirmPairing | Request | 6.2 |
| 274 | CancelPairing | Request | 6.2 |
| 275 | GetPairing | Request | 6.2 |
| 276 | ListKnownUsers | Request | 6.2 |
| 277 | ForgetUser | Request | 6.2 |
| 288 | ListDevices | Request | 6.3 |
| 289 | RemoveDevice | Request | 6.3 |
| 304 | ListPackages | Request | 6.4 |
| 305 | GetPackage | Request | 6.4 |
| 306 | CreatePackage | Request | 6.4 |
| 307 | UpdatePackage | Request | 6.4 |
| 308 | FollowPackage | Request | 6.4 |
| 309 | UnfollowPackage | Request | 6.4 |
| 310 | SetPackagePolicy | Request | 6.4 |
| 311 | ListGrants | Request | 6.4 |
| 312 | SetGrant | Request | 6.4 |
| 313 | RevokeGrant | Request | 6.4 |
| 314 | ListHistory | Request | 6.4 |
| 315 | GetEntry | Request | 6.4 |
| 316 | GetObject | Request | 6.4 |
| 317 | Materialise | Request | 6.4 |
| 318 | Fetch | Request | 6.4 |
| 319 | ListStored | Request | 6.4 |
| 352 | ListPins | Request | 6.5 |
| 353 | Pin | Request | 6.5 |
| 354 | Unpin | Request | 6.5 |
| 355 | GetStore | Request | 6.5 |
| 356 | SetStoreBudget | Request | 6.5 |
| 357 | ReleasePins | Request | 6.5 |
| 368 | ListPeers | Request | 6.6 |
| 369 | ListSessions | Request | 6.6 |
| 370 | ListDiscovered | Request | 6.6 |
| 371 | ListStaticPeers | Request | 6.6 |
| 372 | AddStaticPeer | Request | 6.6 |
| 373 | RemoveStaticPeer | Request | 6.6 |
| 374 | Dial | Request | 6.6 |
| 384 | ListServices | Request | 6.7 |
| 385 | ServiceCall | Request | 6.7 |
| 400 | Subscribe | Request | 7.1 |
| 401 | Unsubscribe | Request | 7.1 |
| 416 | ListOperations | Request | 8.5 |
| 417 | GetOperation | Request | 8.5 |
| 418 | CancelOperation | Request | 8.5 |
| 432 | ListSeries | Request | 10.3 |
| 433 | ListKeys | Request | 10.3 |
| 434 | QueryTelemetry | Request | 10.3 |
| 435 | GetTotal | Request | 10.3 |
| 512 | RegisterKind | Request (service) | 9.2 |
| 513 | WriteEntry | Request (service) | 9.4 |
| 514 | PutObject | Request (service) | 9.5 |
| 515 | PutFile | Request (service) | 9.5 |
| 516 | GetStorePath | Request (service) | 9.7 |
| 517 | AddTelemetry | Request (service) | 9.9 |
| 518 | Emit | Request (service) | 9.10 |
| 1024 | PackageChanged | Event | 7.3 |
| 1025 | EntryStored | Event | 7.3 |
| 1026 | PeerChanged | Event | 7.3 |
| 1027 | TransferChanged | Event | 7.3 |
| 1028 | OperationChanged | Event | 7.3 |
| 1029 | PairingChanged | Event | 7.3 |
| 1030 | TelemetrySample | Event | 7.3 |
| 1031 | ServiceEvent | Event | 7.3 |
| 1032 | UserChanged | Event | 7.3 |
| 1033 | StoreChanged | Event | 7.3 |
| 1034 | SubscriptionLapsed | Event | 7.3 |
| 1536 | (reserved; `ValidatePayload` in draft 1, removed) | Callback | 9.8 |
| 1537 | ForwardedCall | Callback | 9.8 |

Ranges (§3.3): 7 to 31 session extensions (a violation when unknown); 34 to 255 reserved (a violation);
259 to 271, 278 to 287, 290 to 303, 320 to 351, 358 to 367, 375 to 383, 386 to 399, 402 to 415, 419 to 431 and
436 to 511 general-request extensions, grouped as their neighbours; 519 to 767 service-request extensions; 768
to 1023 request extensions of either role (all answered `UnknownRequest` when unknown); 1035 to 1535 event
extensions (ignored when unknown); 1536 reserved and 1538 to 2047 callback extensions (answered
`UnknownRequest` when unknown); 2048 to 65535 never assigned (a violation).

### 14.2 Roles

| Value | Role | § |
|---|---|---|
| 1 | user | 6 |
| 2 | service | 9 |

### 14.3 Bye reasons

| Value | Reason | Sent by |
|---|---|---|
| 0 | Unspecified | either |
| 1 | Shutdown | the Host, after `Drain` (§4.5) |
| 2 | Duplicate | the Host, a second connection for a live service name (§4.2) |
| 3 | Violation | the Host (§11.3) |
| 4 | Refused | the Host, `Hello` not accepted; `detail` is `version`, `service` or `busy` (§4.2) |
| 5 | Unresponsive | the Host (§4.4, §7.4) |
| 6 | Done | a client that is finished (§4.3) |

### 14.4 Capability flags

Client flags, in `Hello.capabilities`:

| Bit | Meaning |
|---|---|
| 0 | the client wants partial telemetry samples (§10.4) |
| 1 to 31 | reserved; MUST be sent as 0, ignored when received |

Host flags, in `HelloAck.capabilities`:

| Bit | Meaning |
|---|---|
| 0 | the store directory is readable by this connection's user; `GetStorePath` succeeds (§9.7) |
| 1 | the Host runs under another OS account than this connection's user (§13); paths are still opened as the client's user |
| 2 | the Host keeps telemetry (§10); clear on a Host configured without it, and then the requests of §10, `AddTelemetry` and a `telemetry` subscription are `NotFound` |
| 3 to 31 | reserved; MUST be sent as 0, ignored when received |

### 14.5 Topics

| Id | Topic | Event | § |
|---|---|---|---|
| 1 | packages | PackageChanged | 7.2 |
| 2 | entries | EntryStored | 7.2 |
| 3 | peers | PeerChanged | 7.2 |
| 4 | transfers | TransferChanged | 7.2 |
| 5 | operations | OperationChanged | 7.2 |
| 6 | pairing | PairingChanged | 7.2 |
| 7 | telemetry | TelemetrySample | 7.2 |
| 8 | service | ServiceEvent | 7.2 |
| 9 | users | UserChanged | 7.2 |
| 10 | store | StoreChanged | 7.2 |

11 to 255 are reserved for extensions; an unknown topic is `Invalid`.

### 14.6 Operation kinds

| Value | Kind | § |
|---|---|---|
| 1 | Materialise | 8.6 |
| 2 | Fetch | 8.6 |
| 3 | PutFile | 8.6 |

4 to 255 reserved.

### 14.7 Operation states

| Value | State |
|---|---|
| 1 | Running |
| 2 | Done |
| 3 | Failed |
| 4 | Cancelled |

### 14.8 Pin reasons

| Value | Reason | Source (§5.11) | Held by |
|---|---|---|---|
| 1 | Head | the Package id; for a Roster the user id | the Host (SPEC.md §11.2): Entries, Manifests, Grants and Rosters of followed Packages |
| 2 | Policy | the Package id | the Host (SPEC.md §11.2) |
| 3 | User | all zero | the user role (§6.5) |
| 4 | Service | `H(service name)` | the service role (§9.6) |
| 5 | Outbound | the Package id | the Host, on objects an Entry written here references, until a peer has fetched them (§9.4, SPEC.md §11.2) |

6 to 255 reserved.

### 14.9 Status codes

The table of §11.1: 0 to 20 assigned, 21 to 32 767 reserved, 32 768 to 65 535 service-defined.

### 14.10 Pairing modes and states

| Mode | Value | This side | Needs |
|---|---|---|---|
| ShowUser | 1 | shows a code; pairs with another user | an identity |
| JoinUser | 2 | enters the other user's code | an identity |
| ShowDevice | 3 | shows this device's code to be enrolled | no identity |
| AddDevice | 4 | enters a device's code and adds it to the own Roster | the user key |

| State | Value |
|---|---|
| Waiting | 1 |
| Confirm | 2 |
| Confirming | 3 |
| Paired | 4 |
| Failed | 5 |
| Cancelled | 6 |

### 14.11 Peer sources and states

`Peer.sources` bit flags: bit 0 mDNS/DNS-SD, bit 1 UDP broadcast, bit 2 static address, bit 3 an established
session; bits 4 to 7 reserved.

| State | Value | Meaning |
|---|---|---|
| Discovered | 1 | seen, no session |
| Dialling | 2 | a dial is in progress |
| Established | 3 | a session is established |
| Refused | 4 | the peer closed before its `Hello` (SPEC.md §6.2) |
| Backoff | 5 | not dialled until `backoffUntil` (SPEC.md §4.4, §12.1) |
| Unknown | 6 | not in any Roster this Host holds; never dialled |

`PeerChanged.change`, the peer change kinds:

| Value | Change | Meaning |
|---|---|---|
| 1 | Discovered | discovery saw the device |
| 2 | Dialling | a dial started |
| 3 | Established | a session was established; `session` present |
| 4 | Closed | a session ended; `session` present |
| 5 | RefusedByPeer | the peer closed before its `Hello` (SPEC.md §6.2) |
| 6 | RefusedHere | this node refused the device (TLS check or admission) |
| 7 | Forgotten | its user was forgotten (§6.2) |

8 to 255 reserved.

### 14.12 Package change kinds

| Value | Change | `PackageChanged` carries |
|---|---|---|
| 1 | Followed | created here, or started following; `state` Followed |
| 2 | Manifest | a new current Manifest, for a Listed Package the first one |
| 3 | Grant | a new current Grant, in `grant` |
| 4 | Head | a new or changed Head, in `head` |
| 5 | Policy | a new local policy |
| 6 | Unfollowed | stopped following; the record shows `state` Listed, or the last record of a Package that is dropped |
| 7 | Listed | a peer listed a Package this Host did not know (§5.4, decision 20); `state` Listed |

8 to 255 reserved.

### 14.13 Telemetry series

| Series | Kind | Key |
|---|---|---|
| `peer.bytes.in`, `peer.bytes.out` | counter | peer device id, hex |
| `peer.objects.in`, `peer.objects.out` | counter | peer device id, hex |
| `peer.refused` | counter | peer device id, hex: refusals of that device by this node (TLS and admission) |
| `peer.violations` | counter | peer device id, hex |
| `package.bytes.in`, `package.bytes.out` | counter | Package id, hex |
| `package.objects.in`, `package.objects.out` | counter | Package id, hex |
| `transfer.bytes.in`, `transfer.bytes.out` | counter | `<peer device id>:<Package id>`, hex |
| `package.heads.known`, `package.heads.held` | gauge | Package id, hex |
| `package.objects.wanted`, `package.objects.held` | gauge | Package id, hex |
| `store.bytes`, `store.objects`, `store.pinned.bytes`, `store.budget` | gauge | empty |
| `store.evicted.bytes` | counter | empty |
| `pins.bytes` | gauge | the pin reason name: `Head`, `Policy`, `User`, `Service`, `Outbound` |
| `sessions.open` | gauge | `in` or `out` |
| `admission.refused` | counter | the port: `mesh`, `outbound`, `local` |
| `discovery.seen` | counter | `mdns`, `broadcast`, `static` |
| `local.requests` | counter | the client's `Hello.name` |
| `local.violations` | counter | the client's `Hello.name` |
| `edge.crossings.in`, `edge.crossings.out` | counter | `<link id>:<Package id>`; reserved for the Edge (DESIGN.md §16) |
| `svc.<service>.<name>` | counter | the service's choice (§9.9) |

Series names not listed and not under `svc.` are reserved for extensions; `ListSeries` is authoritative for a
running Host.

### 14.14 Local retention policy

| Value | Policy |
|---|---|
| 0 | the Manifest's `retention` when it is not 0, else the device class default (SPEC.md §11.2) |
| 1 | everything: the desktop row of SPEC.md §11.2 |
| 2 | latest only: the phone row of SPEC.md §11.2 |

3 to 255 reserved.

## 15. Conformance

A Host conforms to `axiom-mesh-local/1` if it satisfies every MUST in this document addressed to it; a client,
every MUST addressed to clients, services or its role. A test suite for conformance covers, at least: every
message encoded and decoded at every byte split, with ProtoStream's harness; every limit of §12 refused one
over and accepted at the limit, with the violation-versus-error distinction of §11; every request of §6, §7,
§8, §9 and §10 answered with the stated OK body and with every listed error against a fixed Host state; the
class rules of §3.3 against a frame of each class from each side; `Hello` refused for each reason of §4.2,
`Duplicate` against a live service name, an in-process name claimed on the pipe, and a spawned name with a
wrong token; a `WriteEntry` with no parents producing an Entry that verifies under SPEC.md §8.6, orders under
§9.3 and is faultless under §8.6 against an in-memory node, for the four parent cases of §9.4, the two-parent
case against a Package whose highest clock is 2^32 + 1 above the own Head; a `Message`, `Version` and
`Redaction` payload that does not parse to its end answered `Invalid` and the same Entry from a peer dropped
as faulty; a `Redaction` setting `Entry.redacted`; the replay rule of §7.2 delivering exactly the Entries with
`storeSeq` ≥ `fromSeq` once and in order, before and across a lapse of §7.4, in events and in bytes; a client
discarding its index on a changed `storeEpoch`; the slow-consumer rule leaving other subscriptions and
requests intact; an operation cancelled by `CancelOperation`, by `Drain` and by a closed connection, with
`status` `Cancelled`; an `Outbound` pin released after the object was sent once; direct reads of §9.7 against
an object that is then evicted while a reader holds it open; and the pipe admitted for the Host's OS user and
refused for another, by access list and by process identity, on each desktop platform. Test vectors are
published with the reference implementation and are part of this specification once they exist.

## Appendix A. Layouts at a glance

```
frame          := length:u32 messageId:u16 payload
Response       := correlation:u32 status:u16 body;   Reply := callback:u32 status:u16 body
Request        := correlation:u32 body;              Event := subscription:u32 body;   Callback := callback:u32 body

Hello          := version:u16 role:u8 name:string clientVersion:string capabilities:u32 token:bytes<32>
HelloAck       := version:u16 hostVersion:string role:u8 capabilities:u32 identity:Identity storeEpoch:id storeSeq:u64
Bye            := reason:u8 detail:string;   Ping | Pong := nonce:u64;   Drain := grace:u32

Identity       := present:bool userId:id userName:string userKeyPresent:bool deviceId:id deviceName:string rosterSeq:u64 created:time
User           := userId:id name:string self:bool knownSince:time rosterSeq:u64 devices:u16
Device         := deviceId:id userId:id name:string addedAt:time removed:bool self:bool lastSeen:time
Package        := packageId:id state:u8 ownerId:id kind:u16 name:string created:time retention:u8 manifestSeq:u64 manifestHash:hash
                  branches:list<Branch> listedBy:u16 policy:u8 rights:u8 heads:list<Head> entries:u64 latestClock:u64
Grant          := hash:hash packageId:id granteeId:id rights:u8 seq:u64
Entry          := hash:hash storeSeq:u64 stored:time packageId:id deviceId:id clock:u64 parents:list<hash> kind:u16 payload:blob redacted:bool
EntryKey       := clock:u64 deviceId:id hash:hash
Peer           := deviceId:id userId:id known:bool addresses:list<string> sources:u8 firstSeen:time lastSeen:time state:u8 backoffUntil:time violations:u32
Session        := deviceId:id userId:id direction:u8 remote:string local:string since:time lastReceived:time tlsSuite:string state:u8
                  service:string bytesIn:u64 bytesOut:u64 objectsIn:u64 objectsOut:u64 wantsOut:u16 wantsIn:u16
StaticPeer     := address:string lastDialled:time lastResult:u8
Store          := path:path objects:u64 bytes:u64 budget:u64 pinnedBytes:u64 evictableBytes:u64 heldBytes:u64
Pin            := hash:hash reason:u8 source:id since:time size:u64;   PinKey := hash:hash reason:u8 source:id
Service        := name:name connected:bool inProcess:bool version:string kinds:list<u16> entryKinds:list<u16>
Operation      := operation:u32 kind:u16 state:u8 started:time updated:time done:u64 total:u64 unit:u8 message:string status:u16 result:blob
Error          := message:string detail:string
Pairing        := pairing:u32 mode:u8 state:u8 code:string sas:string remoteUserId:id remoteName:string remoteDeviceId:id expires:time failure:string
Transfer       := peer:id packageId:id hash:hash direction:u8 size:u64 state:u8 at:time
TelemetrySeries := name:name kind:u8;   TelemetryPoint := minute:time value:u64
Filter         := packageId:id deviceId:id userId:id kinds:list<u16> operation:u32 service:string series:string key:string fromSeq:u64

GetIdentity    := -                                   → identity:Identity
CreateIdentity := userName:string deviceName:string   → identity:Identity
RenameDevice   := deviceId:id name:string             → device:Device
StartPairing   := mode:u8 code:string                 → pairing:Pairing
ConfirmPairing | CancelPairing := pairing:u32          → pairing:Pairing;   GetPairing := - → pairing:Pairing
ListKnownUsers := -                                   → users:list<User>
ForgetUser     := userId:id                           → -
ListDevices    := userId:id                           → devices:list<Device>
RemoveDevice   := deviceId:id                         → device:Device
ListPackages   := after:optional<id> kinds:list<u16> ownerId:optional<id> followedOnly:bool → packages:list<Package> more:bool
GetPackage | UnfollowPackage := packageId:id          → package:Package
CreatePackage  := kind:u16 name:string retention:u8   → package:Package
UpdatePackage  := packageId:id name:optional<string> retention:optional<u8> branches:optional<list<Branch>> → package:Package
FollowPackage | SetPackagePolicy := packageId:id policy:u8 → package:Package
ListGrants     := packageId:id                        → grants:list<Grant>
SetGrant       := packageId:id granteeId:id rights:u8 → grant:Grant;   RevokeGrant := packageId:id granteeId:id → grant:Grant
ListHistory    := packageId:id after:optional<EntryKey> reverse:bool kinds:list<u16> limit:u16 → entries:list<Entry> more:bool
GetEntry       := hash:hash                           → entry:Entry
GetObject      := hash:hash                           → object:blob
Materialise    := packageId:id hash:hash path:path existing:u8 → operation:u32
Fetch          := packageId:id hash:hash recursive:bool pin:bool → operation:u32
ListStored     := packageId:id kinds:list<u16> fromSeq:u64 limit:u16 → entries:list<Entry> more:bool
ListPins       := hash:optional<hash> packageId:optional<id> after:optional<PinKey> → pins:list<Pin> more:bool
Pin            := hash:hash                           → pin:Pin;   Unpin := hashes:list<hash> → removed:u16
GetStore       := -                                   → store:Store;   SetStoreBudget := bytes:u64 → store:Store
ReleasePins    := reason:u8 source:id                 → removed:u32
ListPeers | ListDiscovered := -                       → peers:list<Peer>
ListSessions   := -                                   → sessions:list<Session>
ListStaticPeers := -                                  → peers:list<StaticPeer>
AddStaticPeer  := address:string                      → peer:StaticPeer;   RemoveStaticPeer := address:string → -
Dial           := deviceId:id address:string          → -
ListServices   := -                                   → services:list<Service>
ServiceCall    := service:name method:name payload:blob → payload:blob
Subscribe      := subscription:u32 topic:u16 filter:Filter → -;   Unsubscribe := subscription:u32 → -
ListOperations := -                                   → operations:list<Operation>
GetOperation | CancelOperation := operation:u32       → operation:Operation
ListSeries     := -                                   → series:list<TelemetrySeries>
ListKeys       := series:name after:optional<string>  → keys:list<string> more:bool
QueryTelemetry := series:name key:string from:time to:time → points:list<TelemetryPoint>
GetTotal       := series:name key:string              → value:u64 since:time

RegisterKind   := kind:u16 entryKinds:list<u16>       → storeEpoch:id storeSeq:u64 packages:u32
WriteEntry     := packageId:id kind:u16 parents:list<hash> payload:blob references:list<hash> → entry:Entry
PutObject      := object:blob                         → hash:hash stored:bool
PutFile        := path:path                           → operation:u32
GetStorePath   := -                                   → path:path layout:u8
AddTelemetry   := series:name key:string delta:u64    → -
Emit           := event:name packageId:id payload:blob → -

PackageChanged := change:u8 package:Package grant:optional<Grant> head:optional<Head>
EntryStored    := entry:Entry
PeerChanged    := change:u8 peer:Peer session:optional<Session>
TransferChanged := transfer:Transfer;   OperationChanged := operation:Operation;   PairingChanged := pairing:Pairing
TelemetrySample := series:name key:string minute:time value:u64 partial:bool
ServiceEvent   := service:name event:name packageId:id payload:blob
UserChanged    := change:u8 user:User devices:list<Device>
StoreChanged   := store:Store
SubscriptionLapsed := dropped:u32 lastSeq:u64

ForwardedCall  := client:u32 clientName:name method:name payload:blob → payload:blob
```

## Appendix B. Worked example

Alice's laptop runs the Host; its device is L, its store sequence stands at 4 120, and it follows the Channel
`C` of SPEC.md Appendix B, whose newest Entries are `e7` (L's own Head, clock 7) and `e8` (from Alice's phone P,
clock 8). The Chat service is in-process; the app is a separate process on the pipe.

1. **Chat connects.** Over its in-memory pair, before the Host opened the pipe, Chat sends `Hello(1, role 2,
   "chat", "1.0", 0, token 0)`. The configuration lists `chat` as in-process with kind 2 and Entry kinds 2
   and 3, no other `chat` connection is live, so the Host answers `HelloAck(1, "1.0", 2, flags 0b101,
   identity{present, Alice, L, userKeyPresent true}, storeEpoch E, storeSeq 4120)`. Chat sends
   `RegisterKind(kind 2, [2, 3])` with correlation 1 and gets `Response(1, OK, storeEpoch E, storeSeq 4120,
   packages 3)`. Its own index was built under epoch `E` and ends at store sequence 4 100, so it sends
   `Subscribe(subscription 1, entries, filter{kinds [2, 3], fromSeq 4101})` (correlation 2) and
   `Subscribe(2, packages, filter{kinds [2]})` (correlation 3). The Host answers both, then replays twenty
   `EntryStored` events on subscription 1, 4 101 to 4 120, in order; Chat indexes them.
2. **The app connects.** Over the named pipe the app sends `Hello(1, role 1, "app", "1.0", 0b1, token 0)`; the
   Host reads the process identity (Alice's user, process 7, the app's executable) and gets its `HelloAck`. It
   sends `Subscribe(1, packages, filter{})` and `Subscribe(2, entries, filter{})` first, then `GetIdentity` and
   `ListPackages(followedOnly)`, with correlations 1 to 4, all outstanding at once; the responses come back in
   whatever order the Host finishes them, and the app reconciles the listing with any event that arrived
   meanwhile. The app then asks Chat for the rendered timeline with `ServiceCall("chat", "timeline", {C, last
   50})`, which reaches Chat as `ForwardedCall(callback 1, client 7, "app", "timeline", …)`; Chat answers from
   its index with `Reply(1, OK, payload)` and the app gets the `Response`.
3. **Alice types.** The app sends `ServiceCall("chat", "send", {C, "hello"})` (correlation 5). Chat gets
   `ForwardedCall(2, …)` and sends `WriteEntry(C, kind 2, parents [], payload Message{"hello"}, references [])`
   (correlation 4). The Host parses the `Message` (it has no attachments), chooses `clock` 9 (the highest it
   has stored is 8), parent `e7` (L's own Head), signs with L's device key, stores `e9` with store sequence
   4 121, moves L's Head to `e9`, pins it with reason `Head`, and sends `PackageChanged` and `Have` to P and to
   Bob's desktop D. It answers Chat `Response(4, OK, Entry e9)`, and delivers `EntryStored(e9)` on Chat's
   subscription 1 and on the app's subscription 2, and `PackageChanged(Head)` on Chat's subscription 2 and the
   app's subscription 1. Chat indexes `e9` and replies `Reply(2, OK, {e9})`; the app's `Response(5, OK, …)`
   follows. The app already drew the message from its own `EntryStored`.
4. **Phone answers.** P writes `e10` (clock 10, parent `e8`) and the Host receives it over AMP/1. It verifies the
   signature against Alice's Roster, has `e8` stored already, parses the `Message` payload to its end and
   takes the attachment Tree `t` it names, stores `e10` as 4 122, and both subscribers get `EntryStored(e10)`.
   Had the payload not parsed, `e10` would have been faulty content: dropped, P recorded for it, and nothing
   delivered; Chat is never asked.
5. **An attachment.** The app opens `t`, which the phone policy did not fetch here; it sends `Fetch(C, t,
   recursive true, pin false)` and gets operation 1. `OperationChanged` events on a subscription to
   `operations` show `done` climbing in objects; the final one is `Done` with `{objects 5, bytes 3 145 728}`.
   On P, `t` and its chunks carry an `Outbound` pin until this fetch was served, so P's "attachments on
   demand" policy could not have evicted them first. The app reads the ChunkLists and Chunks with `GetObject`
   and renders them.
6. **A slow app.** The app's window is minimised and for twenty seconds it reads its pipe slowly while the
   laptop syncs a large Archive; subscription 2, which has no filter, reaches 256 queued `EntryStored`s of
   that Archive, so the Host drops the queue, ends subscription 2 and queues `SubscriptionLapsed(dropped 257,
   lastSeq 4 380)`. When the app catches up it sees the lapse, sends `Subscribe(2, entries, filter{fromSeq
   4381})` and is current again; its subscription 1 and the connection were never affected, since every frame
   was accepted within 30 seconds and every `Ping` answered.
7. **Shutdown.** The Host stops: it sends `Drain(5000)` to the app and to Chat. The app sends `Bye(Done)` at
   once. Chat finishes writing its index, sends `Bye(Done)`, and the Host's `RegisterKind` for kind 2 is
   released. A connection that had not answered after five seconds would have got `Bye(Shutdown)`.
