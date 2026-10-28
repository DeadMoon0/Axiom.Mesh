# Axiom Mesh Local Protocol, Version 1 (axiom-mesh-local/1)

**Status:** Draft 1, 2026-10-28. Normative for the first implementation. Where this document and
[DESIGN.md](DESIGN.md) §10 differ, this document wins. Where it restates a rule of [SPEC.md](SPEC.md) (AMP/1),
SPEC.md wins and this document only says how that rule is reached over the pipe.

**Abstract.** `axiom-mesh-local/1` is the protocol every program on a device speaks to the Host, the one process
that holds the device's keys, rosters, connections, object store and database. It runs over a pipe the operating
system authenticates, carries typed binary frames with the framing and types of AMP/1, pairs requests with
responses by a correlation id, pushes events on subscriptions so that no client polls, reports long-running work
as operations, and has two roles: **user**, for the app, the command line and any local tool, and **service**,
for the programs that define a Package kind. A service never touches the wire, a key or the database; it writes
Entries the Host signs, stores and replicates, and reads verified objects from the store directory.

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
| **Held**, **Stored**, **Object**, **Entry**, **Head**, **Package**, **Grant**, **Roster**, **Known user**, **Followed Package** | As in SPEC.md §1.2. |

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
4. **A fourth pin reason, `Service`** (§9.6, §14.8), for objects a service put or asked to keep. DESIGN.md §7.3
   and SPEC.md §11.2 list three.
5. **Payload validation is a callback** (§9.8): the Host stores an Entry of a registered kind only after the
   kind's service found the payload well-formed; while no service for the kind is connected, such Entries are
   held like unverifiable objects (SPEC.md §10.6).
6. **Parents when the service names none** (§9.4): the device's own Head; when the device has no Head but holds
   Entries of the Package, the Entry highest in the total order. SPEC.md §9.3 does not say what a device that
   holds Entries but never wrote names as parent; this document does.
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
13. **The Host does not interpret payloads**, so `GetEntry` returns the payload of a withdrawn `Message` too; the
    "MUST NOT show" of SPEC.md §9.4 is met by the Chat service in its own namespace, which is what the app
    renders.
14. **Topics `users` and `store`** exist beside the ones DESIGN.md §10.4 names, because the app shows known users,
    rosters and the store budget live.
15. **Objects on demand** (DESIGN.md §7.3) are fetched by the `Fetch` operation (§6.4, §8.6); `Materialise`
    fetches what it lacks itself.
16. **A Package of any kind may be created**, with or without a service for that kind; the Host carries unknown
    kinds opaquely as SPEC.md §9.2 says.
17. **`GetStorePath` is a service request** (§9.7); user clients read objects through `GetObject`.
18. **Local retention policy** per Package is a value of the same enumeration as the Manifest's `retention`,
    with 0 meaning "the Manifest's value, else the device class" (§14.14).
19. **Live telemetry is per minute by default**; a client that sets capability bit 0 also receives partial
    samples of the current minute at most once per second (§10.4).

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

### 2.2 Authentication

The operating system authenticates: the named pipe's access control list, or the socket file's owner and mode,
admit the OS user the Host serves and nobody else, and the Host MUST obtain and log the connecting process id
where the platform provides it. There is no token, no password and no network port, and a connection that the
operating system admitted is trusted as that user for everything the user role may do.

### 2.3 Connection lifetime

A connection begins when the client connected to the pipe and ends when either side closes it. The client MUST
send `Hello` within 10 seconds of connecting; the Host closes the connection otherwise. When a connection ends,
for whatever reason, everything tied to it ends with it: outstanding requests are never answered, subscriptions
are gone, running operations are cancelled (§8.5), outstanding callbacks are abandoned, and a service's
registered kinds are released (§9.2). Nothing of a connection survives it; the Host keeps no session state
across connections.

A client that loses its connection SHOULD reconnect, waiting 1 second after the first failure and doubling up to
30 seconds, and then re-establish what it needs: subscriptions, and for a service its registration (§9.2). The
Host MUST accept up to 64 user connections at once and one connection per configured service name (§12).

### 2.4 Permissions

The exact access control list of the named pipe, the owner and mode of the socket file and its directory, the
account the Host serves when it runs as a Windows service, and the configuration keys that set all of these
are specified in a separate document, `PIPE-ACCESS.md`; this document requires only the effect stated in §2.2.

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
| `name` | `string(64)` of ASCII letters, digits, `.`, `-` and `_`, never empty; a client, service, method, event or series name |
| `object` | `blob(4194304)`; the bytes of one SPEC.md §8 object |
| `payload` | `blob(1048576)`; bytes the Host carries without interpreting them |

A value outside its bounds in a request or a `Reply` is a violation (§11.3); in a `Response`, `Event` or
`Callback` it is a Host defect the client treats as a lost connection. The canonical form of SPEC.md §5.3
applies: fixed field order, no padding, every list in the order its definition states, and `optional<T>` is the
only conditional field.

### 3.3 Message classes

The message id says which class a frame belongs to, so the receiver knows how to read the envelope before it
knows the message:

| Class | Ids | Sent by | Envelope (the first bytes of the payload) |
|---|---|---|---|
| Session | 1 to 31 | both | none |
| Response | 32 | Host | `correlation: correlation  status: u16  body` |
| Reply | 33 | a service | `callback: callback  status: u16  body` |
| (reserved) | 34 to 255 | — | a violation when received |
| Request | 256 to 1023: user requests 256 to 511, service requests 512 to 767, extensions 768 to 1023 | a client | `correlation: correlation  body` |
| Event | 1024 to 1535 | Host | `subscription: subscription  body` |
| Callback | 1536 to 2047 | Host | `callback: callback  body` |
| (never assigned) | 2048 to 65535 | — | a violation when received |

A client MUST NOT send a Response, Event or Callback; a user client MUST NOT send a Reply; the Host never sends a
Request or a Reply. A frame of a class its sender may not send is a violation.

### 3.4 Requests and responses

A request is a frame in the request range whose payload is a `correlation` followed by the body its section in
§6, §7, §8, §9 or §10 states. The Host answers every request with exactly one `Response` (32) carrying the same
`correlation`: with `status` 0 (`OK`) and the OK body the request's section states, or with a non-zero `status`
from §14.9 and an `Error` record (§5.14) as the body. A request that fails has no effect.

Rules:

- `correlation` MUST NOT be 0 and MUST differ from every correlation of a request on the same connection whose
  `Response` has not yet been sent; a client MAY reuse a correlation once its `Response` arrived. A request that
  breaks this is a violation.
- A client MUST NOT have more than 64 requests outstanding on one connection; the 65th is a violation.
- The Host MAY process requests concurrently and MAY answer them in any order. Two requests on one connection
  that write to the same Package are applied in the order they were received. A client that needs one request's
  effect to be visible to another MUST wait for the first `Response`.
- A request whose message id is in the request range but unknown to the Host is answered with
  `UnknownRequest`; one that the connection's role may not send, with `NotPermitted` (§6 states the roles).
- The Host SHOULD send the `Response` within 10 seconds; work that can take longer is an operation (§8), whose
  starting request is answered at once.
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
(§5.14) when `status` is not 0. A `Reply` whose `callback` names no outstanding callback is a violation. The
Host MUST NOT have more than 64 callbacks outstanding toward one service; §9.8 says what it does past that.

## 4. Session

### 4.1 States

```
            Hello / HelloAck                 Drain
Connected ───────────────────► Ready ─────────────────► Draining
    │                            │                          │
    └──── Bye or close ◄─────────┴────────── Bye ◄──────────┘
```

- **Connected:** the pipe connection exists. The client MUST send `Hello` as its first frame; the Host accepts
  nothing else from it, and sends nothing before `Hello` arrived. Any other first frame, or no `Hello` within
  10 seconds of the connection, is a violation. The Host answers `HelloAck` or `Bye`.
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
HelloAck:
  version:        u16             = 1
  hostVersion:    string(64)      for logs; MUST NOT be interpreted
  role:           u8              the role granted; equals Hello.role
  capabilities:   u32             bit flags, §14.4 (Host flags)
  identity:       Identity        §5.1; present = false before CreateIdentity or enrolment
  storeSeq:       u64             the store sequence of the newest stored Entry; 0 when none
```

On `Hello` the Host checks, in this order, and on the first failure sends `Bye` with the stated reason and
closes:

1. `version` is 1; else `Bye(Refused, "version")`.
2. `role` is 1 or 2; else a violation, `Bye(Violation)`.
3. For role 2, `name` is a service name the Host's configuration lists; else `Bye(Refused, "service")`. A
   client the configuration does not list can only be a user client, whatever it asks for; the Host MUST NOT
   grant a role other than the one asked for.
4. For role 2, no other connection with the same `name` is in Ready or Draining; else `Bye(Duplicate)`. The
   live connection is kept.
5. The connection limits of §12 hold; else `Bye(Refused, "busy")`.

Then the Host sends `HelloAck` and the session is Ready. In-process services (DESIGN.md §10.3) connect through
the same `Hello`; the Host treats them exactly like a program on the pipe.

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
ownerId:        id
kind:           u16             SPEC.md §14.8; unknown kinds are carried
name:           string(255)     from the current Manifest
created:        time            from the current Manifest
retention:      u8              from the current Manifest: 0 default, 1 everything, 2 latest only
manifestSeq:    u64
manifestHash:   hash
branches:       list<Branch>(64)    SPEC.md §8.7, ascending by name
followed:       bool
policy:         u8              the local retention policy, §14.14
rights:         u8              this user's rights: 2 for the owner, else the current Grant's rights, else 0
heads:          list<Head>(256) SPEC.md §7.2, ascending by deviceId; the Heads this Host holds
entries:        u64             stored Entries of this Package
latestClock:    u64             the highest clock among them; 0 when none
```

`Branch` and `Head` are the structures of SPEC.md §8.7 and §7.2, encoded as there.

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
  payload:      payload         the Entry's payload bytes, uninterpreted
EntryKey:
  clock:        u64
  deviceId:     id
  hash:         hash
```

An `EntryKey` is the position of an Entry in the total order of SPEC.md §9.3, `(clock, deviceId, hash)`
ascending. The store sequence is the position in the order this Host stored Entries, across every Package; it
is unique, increasing, never reused, and the Host MUST persist it with the Entry.

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
  source:       id              Head, Policy: the Package id; User: all zero; Service: H(service name) over the name's UTF-8 bytes
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
kinds:          list<u16>(16)   Package kinds it registered, ascending
entryKinds:     list<u16>(64)   Entry kinds it registered, ascending
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
unit:           u8              1 bytes, 2 objects, 3 files
message:        string(256)     for display; MUST NOT be interpreted
status:         u16             §14.9: the reason when state is Failed; else 0
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
role. A request sent by a role not listed is answered `NotPermitted`. Every request may fail with
`UnknownRequest`, `Draining`, `Busy` (§12) or `Invalid` (a value that is in bounds but wrong in substance, named
in `Error.detail`); only further errors are listed. Requests that need an identity fail with `NoIdentity`
before any other check when `Identity.present` is false; those are `StartPairing` in every mode but `ShowDevice`,
`ForgetUser`, every request of §6.3 and §6.4, `Pin`, `Unpin`, `Dial`, `ServiceCall`, and `WriteEntry` (§9.4).

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

`CreateIdentity` creates the user key, the device key, a Roster with `seq` 1 holding this device, and the
device certificate (SPEC.md §2.4). It fails with `AlreadyExists` when an identity is present; a device that is
to join an existing user enrols instead (§6.2). `RenameDevice` produces a new Roster with `seq` one higher and
replicates it; it needs the user key (`UserKeyAbsent`) and fails with `NotFound` for a device that is not the
own user's.

Backing up the user key and restoring it on another device are not requests of this protocol; the key backup
format and the requests that export and import it are specified in a separate document, `KEY-BACKUP.md`.

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
not the current one and with `Invalid` for a `ConfirmPairing` outside state `Confirm`.

Modes `ShowUser` and `JoinUser` need an identity and end, when `Paired`, with the other user known: their key and
current Roster stored, which is a `UserChanged` event. Mode `ShowDevice` requires that no identity is present;
the Host creates the device key and shows it in `code`, and when `Paired` this device holds the user's id, key,
current Roster and its own place in it, so `Identity.present` becomes true with `userKeyPresent` false. Mode
`AddDevice` needs the user key (`UserKeyAbsent`) and, when `Paired`, has produced a Roster with `seq` one higher
that includes the new device.

`ForgetUser` withdraws known-user status: the Host closes every session with the user's devices, deletes the
Roster it holds, unfollows every Package the user owns and releases those Packages' pins, and stops listing it;
Grants the own user issued to that user stay until revoked. It fails with `Invalid` for the own user and with
`NotFound` for a user that is not known.

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
`Bye(Unauthorized)` (SPEC.md §8.8) and replicates the Roster. It needs the user key (`UserKeyAbsent`) and fails
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
  packageId:    id              a followed Package through which the object is held
  hash:         hash            a Tree or a ChunkList
  path:         path            the directory to write the Tree into, or the file to write the ChunkList to
  existing:     u8              1 fail when path exists, 2 replace it, 3 write into it, keeping what is not in the Tree
  OK:   operation: operation            kind Materialise, §8.6

Fetch (318), U S:
  packageId:    id              a followed Package through which the object is held
  hash:         hash
  recursive:    bool            also the objects it references, through Trees and ChunkLists
  pin:          bool            pin what was fetched with reason User (user role) or Service (service role)
  OK:   operation: operation            kind Fetch, §8.6

ListStored (319), U S:
  kinds:        list<u16>(16)   Entry kinds; empty = every kind in the user role, the registered kinds in the service role
  fromSeq:      u64             the first store sequence to return; 0 reads as 1
  limit:        u16             1 to 64
  OK:   entries: list<Entry>(64)        ascending by storeSeq
        more:    bool
```

Rules:

- `CreatePackage` produces a Manifest with `seq` 1 for a new Package id derived as SPEC.md §2.3 says, owned by
  the own user, followed, with `policy` 0. It needs the user key (`UserKeyAbsent`). Any `kind` is allowed
  (§1.6, decision 16).
- `UpdatePackage` produces a Manifest with `seq` one higher carrying the given fields and the current values of
  the others; an absent field is unchanged. The owner only (`NotOwner`), with the user key (`UserKeyAbsent`).
  `branches` names a head per branch as the owner's declaration (SPEC.md §9.5).
- `FollowPackage` applies to a Package this Host knows through a listing of a peer and is granted; a Package
  the Host knows nothing of is `NotFound`. `UnfollowPackage` releases the Package's `Head` and `Policy` pins; its
  Entries and objects become evictable. A Package the own user owns is always followed; `UnfollowPackage` on it
  is `Invalid`.
- `SetGrant` and `RevokeGrant` produce a Grant with `seq` one higher than the current Grant for that grantee, or
  1; `RevokeGrant` is `SetGrant` with `rights` 0. The owner only (`NotOwner`), with the user key
  (`UserKeyAbsent`); the grantee MUST be a known user (`NotFound`) other than the owner (`Invalid`).
- `ListHistory` walks the stored Entries of the Package in the total order of SPEC.md §9.3. A page is cut so that
  its frame stays within §3.1; `limit` and the list maximum of 64 bound the count, they do not guarantee fit.
  `after` names an Entry that need not be stored any more; paging continues after its key.
- `GetEntry` and `GetObject` return only what is stored; a held, evicted or never seen object is `NotFound`.
  `GetObject` returns an object of any tag, Rosters included.
- `Materialise` writes under the rules of SPEC.md §11.4 and §8.4: it fetches what it lacks through the
  Package's peers as `Fetch` does, writes a file only when every chunk is held, verifies the ChunkList's `size`,
  applies executable mode where the file system supports it, and writes symlinks per device policy. With
  `existing` 1 an existing path fails the operation with `AlreadyExists`; with 2 the path is deleted first;
  with 3 files in the Tree replace files of the same name and other files stay. Conflicts of SPEC.md §8.4 are
  counted in the result and named in the operation's `message`.
- `Fetch` wants the object and, when `recursive`, everything it references, from peers that may serve it
  (SPEC.md §10.3, §10.4); objects already stored are counted as done at once.
- `ListStored` walks Entries across every Package in store-sequence order; it exists so that an indexer
  catches up after it was away (§9.3).

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
  hash:         hash
  OK:   empty

GetStore (355), U S:                    empty
  OK:   store: Store

SetStoreBudget (356), U:
  bytes:        u64             0 = the device default
  OK:   store: Store
```

`Pin` in the user role creates a pin with reason `User` and source all zero; in the service role, reason
`Service` and the service's source (§5.11); it is idempotent. `Unpin` removes that role's pin on the object and
is `NotFound` when there is none; pins of other reasons are never removed through the pipe. `Pin` on an object
that is not stored is `NotFound`. A budget change may start eviction (SPEC.md §11.3) at once; `GetStore` shows
the result over time, and topic `store` pushes it.

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

`AddStaticPeer` fails with `AlreadyExists` and `RemoveStaticPeer` with `NotFound`; a syntactically wrong address
is `Invalid`. `Dial` with both fields empty is `Invalid`; with a device that is not known it is `NotFound`;
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
service's document. Errors: `ServiceUnavailable` when the service is not configured or not connected,
`ServiceTimeout` when it did not reply within 60 seconds, `Busy` when 64 callbacks are outstanding toward it.

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
most 64 subscriptions (`Busy`). An unknown topic or a filter field the topic does not read is `Invalid`. The
Host MUST send the `Response` to `Subscribe` before the first event of the subscription, and MUST NOT send an
event of a subscription after the `Response` to its `Unsubscribe`. Events reflect changes that happen after the
`Response` to `Subscribe`, except where a topic says it replays.

### 7.2 Topics

| Topic | Id | Filter fields read | Event | Delivered when |
|---|---|---|---|---|
| `packages` | 1 | `packageId`, `kinds` (Package kinds) | `PackageChanged` | a Package is created, followed, unfollowed, gets a new current Manifest or Grant, a new or changed Head, or a new local policy; in the order the changes happened |
| `entries` | 2 | `packageId`, `kinds` (Entry kinds), `fromSeq` | `EntryStored` | an Entry is stored; in store-sequence order, each stored Entry once. With `fromSeq` > 0 the Host first sends every stored Entry matching the filter with `storeSeq` ≥ `fromSeq`, in order, then continues live without a gap or a repeat; when more than 256 stored Entries match, `Subscribe` fails with `GapTooLarge` and the client pages with `ListStored` first |
| `peers` | 3 | `deviceId` | `PeerChanged` | a device is discovered, dialled, establishes a session, loses one, refuses or is refused, or is forgotten; in the order it happened |
| `transfers` | 4 | `packageId`, `deviceId` (the peer) | `TransferChanged` | an object is wanted, received, sent, not found, cancelled or dropped; in the order it happened |
| `operations` | 5 | `operation` | `OperationChanged` | an operation of this connection starts, progresses (at most once per second per operation), or ends; every state change is delivered, a progress change may be coalesced |
| `pairing` | 6 | none | `PairingChanged` | the current pairing changes state or a field |
| `telemetry` | 7 | `series`, `key` | `TelemetrySample` | a minute closes, for every `(series, key)` that changed within it, and (client capability bit 0) partial samples, §10.4; ascending by minute, then by series and key as byte strings |
| `service` | 8 | `service`, `packageId` | `ServiceEvent` | a service calls `Emit` (§9.9); in the order of the calls |
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
  change:       u8              1 discovered, 2 dialling, 3 established, 4 closed, 5 refused by the peer, 6 refused by this node, 7 forgotten
  peer:         Peer
  session:      optional<Session>       present for 3 and 4

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
  lastSeq:      u64             topic entries: the storeSeq of the last event delivered; else 0
```

### 7.4 Queues and the slow-consumer rule

The Host queues the events of each subscription separately and writes them to the connection as the pipe
accepts bytes; it MUST NOT block its own work on a client. A subscription's queue holds at most 256 events.
When a 257th event would be queued, the Host discards the queue, ends the subscription, and queues in its place
one `SubscriptionLapsed` naming how many events were discarded (the queued ones plus the one that did not fit)
and, for topic `entries`, the store sequence of the last `EntryStored` that was delivered, so that the client
re-subscribes with `fromSeq` one higher. A lapse ends only that subscription; the connection, its other
subscriptions and its requests are unaffected.

A replay (`fromSeq`) is sent as part of the queue: the Host does not generate more than 256 events for it, so a
replay that fits never lapses on its own, and live events that arrive during a replay count against the same
bound. Across the whole connection, the Host MUST NOT hold more than 64 MiB of frames waiting to be written;
when it would, or when a frame could not be written within 30 seconds, it closes the connection with
`Bye(Unresponsive)`.

## 8. Operations

### 8.1 Model

An operation is work the Host does for one connection that may take longer than a response should: writing a
tree to disk, fetching objects from peers, chunking a file. A request that starts one (`Materialise`, `Fetch`,
`PutFile`) is answered at once with the Host-chosen `operation` id, unique for the connection's lifetime, and
the `Operation` record (§5.13) then changes until it ends. An operation belongs to the connection that started
it: no other connection sees it, and it is cancelled when the connection ends. A connection has at most 16
operations running at once (`Busy`); the Host keeps the records of the last 64 ended operations of a connection
and drops the oldest beyond that.

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
| Cancelled | 4 | ended by `CancelOperation`, `Drain` or the connection's end; what was done stays done |

An ended operation never changes again.

### 8.3 Progress

While Running the Host updates `done` and, when it learns it, `total`, and emits `OperationChanged` on
subscriptions of topic `operations` at most once per second per operation; the final event of an operation is
always emitted. A client without a subscription polls with `GetOperation`.

### 8.4 Completion

An operation ends with `state` Done, Failed or Cancelled and exactly one final `OperationChanged`. Done carries
the result body of its kind (§8.6) in `result`; Failed carries a `status` from §14.9.

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

| Kind | Value | Started by | `unit` | Result body |
|---|---|---|---|---|
| Materialise | 1 | `Materialise` (§6.4) | 1 bytes | `files: u32  bytes: u64  conflicts: u32` |
| Fetch | 2 | `Fetch` (§6.4) | 2 objects | `objects: u32  bytes: u64` |
| PutFile | 3 | `PutFile` (§9.5) | 1 bytes | `hash: hash  size: u64  chunks: u32` (the ChunkList) |

Values 4 to 255 are reserved for extensions.

## 9. Service role

### 9.1 What a service is

A service is a client in the service role that defines a Package kind and the Entry kinds in it: Sync defines
`Archive` with `Version`; Chat defines `Channel` with `Message` and `Redaction` (SPEC.md §14.7, §14.8). It is
written against this protocol only (DESIGN.md §10.3). In the first release Sync and Chat run inside the Host
process, connected to the local port over an in-memory stream pair; the contract below MUST NOT depend on that,
and a service that runs as its own program on the pipe sees exactly the same messages. A service may send
every request of §6 marked **S**, and the requests of this section.

A service that connects does, in this order: `Hello` with role 2 and its name; `RegisterKind` for each of its
Package kinds; `Subscribe` to `entries` for its Entry kinds with `fromSeq` set to one past the last store
sequence it has indexed (after `ListStored` when the gap is large), and to `packages` for its Package kinds.
From then on it receives everything it needs as events.

### 9.2 `RegisterKind` (512)

```
RegisterKind (512), S:
  kind:         u16             a Package kind, SPEC.md §14.8
  entryKinds:   list<u16>(16)   the Entry kinds of this Package kind, ascending, non-empty
  OK:   storeSeq: u64           the store sequence of the newest stored Entry; 0 when none
        packages: u32           followed Packages of this kind
```

A kind is held by at most one connected service; a kind another connected service registered is `KindTaken`,
and an Entry kind another registered kind lists is `Invalid`. Registering a kind this service already holds is
`OK` and changes nothing. A registration lasts while the connection does and is released when it ends.
`WriteEntry`, `ValidatePayload` and `Emit` are scoped to the registered kinds (`KindNotRegistered`).

### 9.3 The streams a service receives

Through topic `entries` with its Entry kinds in the filter, a service receives every Entry of its kinds the
Host stores, from any peer and from its own `WriteEntry` alike, once, in store-sequence order, with the
replay rule of §7.2 for catch-up. Through topic `packages` with its Package kinds in the filter it receives
every follow, unfollow, Manifest, Grant, Head and policy change of Packages of its kinds. The Host MUST deliver
these on the same subscription machinery as for a user client, with the queue bound and the lapse rule of
§7.4; a service that lapses re-subscribes with `fromSeq` from `SubscriptionLapsed.lastSeq` plus one, paging
with `ListStored` when the gap exceeds 256.

### 9.4 `WriteEntry` (513)

```
WriteEntry (513), S:
  packageId:    id              a followed Package of a kind this service registered
  kind:         u16             an Entry kind this service registered for that Package kind
  parents:      list<hash>(2)   ascending as byte strings; empty = the Host chooses
  payload:      payload
  OK:   entry: Entry
```

The Host builds the Entry object of SPEC.md §8.6 as follows and then signs it with the device key under domain
`axiom-mesh/entry/1`:

1. `packageId`, `kind` and `payload` as given; `deviceId` the own device.
2. `parents`: as given, when given; the Host checks that each is a stored Entry of the same `packageId`
   (`Invalid` otherwise). When empty: the own device's Head of the Package, if it has one; else, when the Host
   holds any Entry of the Package, the stored Entry highest in the total order; else none. A service writing a
   `Version` to an Archive with more than one leaf MUST name the leaves it merges itself (SPEC.md §9.5); the
   Host does not know branches.
3. `clock`: one more than the highest `clock` of any Entry the Host holds for the Package, or 0 when it holds
   none (SPEC.md §9.3). When that value would not be well-formed under SPEC.md §8.6, the request fails with
   `ClockExhausted` and the Host surfaces it to the user.

Before signing the Host checks that the own user may write (owner or current `Write` grantee, and this device
not removed; `NotWritable`), that the Package is followed (`NotFound`), and that the result is well-formed and
would be valid under SPEC.md §8.6 and faultless under §8.6 and §9.3 once its parents are considered; a result
that would not be is `Invalid`. The Host does not ask the service to validate its own payload. Then it stores
the Entry, assigns its store sequence, updates its Head, pins the Entry with reason `Head`, pins the payload
objects the Package's policy asks for with reason `Policy` as far as it holds them, and replicates
(`PackageChanged` and `Have`, SPEC.md §10.5). Objects the payload references MUST already be stored
(`PutObject`, `PutFile`) when the policy wants them; the Host does not fetch what a local writer referenced.
The `OK` body is the stored Entry with its hash and store sequence; the same Entry arrives on the service's
`entries` subscription.

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

`PutFile` reads the file at `path` with the Host's own permissions, cuts it into Chunks of exactly 1 MiB except
the last (SPEC.md §8.2), stores each Chunk and the ChunkList (SPEC.md §8.3), pins every one of them with reason
`Service` for this service, and ends with the ChunkList's hash in the result. A file that changes while it is
read fails the operation with `IoError`. A zero-length file produces a ChunkList with no chunks.

The RECOMMENDED pattern for a service is: put the objects, write the Entry that references them, then `Unpin`
them; from then on the Package's `Policy` pins hold what the policy wants and the rest is evictable, which is
what the user's retention choice means.

### 9.6 Pins with reason `Service`

A service pins with `Pin` and `Unpin` (§6.5); its pins have reason `Service` (§14.8) and the source of §5.11,
and are independent of every other reason. The Host releases every pin of a service when the service's
connection ends only if the Host's configuration says so for that service; by default they stay, because an
out-of-process service may restart. `Fetch` with `pin` true pins with reason `Service` when called by a service.

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
stored" (never stored, or evicted since), falling back to `GetObject` or `Fetch`; it MUST NOT write, rename or
delete anything in the directory, and MUST NOT list the directory to learn what exists (`ListPins`, `GetEntry`
and the records say what exists). The Host fails `GetStorePath` with `NotPermitted` when the directory is not
readable by clients (Host capability bit 0 clear, §14.4).

### 9.8 Callbacks

```
ValidatePayload (1536):
  packageId:    id
  deviceId:     id              the writer
  clock:        u64
  kind:         u16
  payload:      payload
  Reply OK:     empty
  Reply status Invalid:  the payload does not parse exactly to its end, or breaks the kind's rules

ForwardedCall (1537):
  client:       u32             the Host's number for the calling connection, for logs
  clientName:   name            the caller's Hello.name
  method:       name
  payload:      payload
  Reply OK:     payload: payload
  Reply status: any code of §14.9 or a service-defined code (§11.2), returned to the caller as its status
```

**`ValidatePayload`.** Before the Host stores an Entry received from a peer whose `kind` a connected service
registered, and after that Entry verified under SPEC.md §8.6 and its parents are held, the Host sends
`ValidatePayload` to that service. `OK` makes the Entry storable; `Invalid` makes it faulty content (SPEC.md
§12.2): dropped, never forwarded, the sender recorded. While no service for the kind is connected, or the
service has not replied within 10 seconds, the Entry stays held under the held-buffer rules of SPEC.md §10.6
and the Host asks again when a service for the kind connects, and at most once a minute otherwise. Entries of
kinds no configured service lists are stored opaquely without a callback (SPEC.md §8.5). Entries this Host wrote
through `WriteEntry` are never sent for validation.

**`ForwardedCall`.** A `ServiceCall` (§6.7) from a user client, forwarded verbatim. The service MUST reply
within 60 seconds; the Host answers the caller `ServiceTimeout` otherwise and ignores a later reply. The service
answers with its own payload, or with a status, which the Host passes to the caller unchanged, `Error` record
included.

When 64 callbacks are outstanding toward a service, the Host queues further `ValidatePayload`s (the Entries stay
held) and answers further `ServiceCall`s with `Busy`.

### 9.9 `Emit` (518)

```
Emit (518), S:
  event:        name            the service's own event name
  packageId:    id              the Package it concerns; all zero when none
  payload:      payload
  OK:   empty
```

Publishes a `ServiceEvent` to every subscription of topic `service` whose filter matches this service and the
Package. This is how a service's namespace pushes (a timeline changed, a folder's sync state moved) without a
user client polling `ServiceCall`. A Package id that is not a followed Package of a kind this service registered
is `Invalid`; all zero is always allowed.

### 9.10 `AddTelemetry` (517)

```
AddTelemetry (517), S:
  series:       name            MUST start with "svc." followed by this service's name and "."
  key:          string(192)
  delta:        u64
  OK:   empty
```

Adds `delta` to the counter `(series, key)` for the current minute and to its lifetime total (§10). A series
outside the service's prefix is `NotPermitted`. Services have counters only; gauges are the Host's.

## 10. Telemetry

### 10.1 Series and keys

A telemetry **series** is a named measurement with a kind, counter or gauge; a **key** names what is measured
within the series (a peer, a Package, a peer and a Package, a port, nothing). The series the Host keeps are
listed in §14.13 with their key; a service adds counters under its own prefix (§9.10). A key is at most 192
bytes so that a peer device id and a Package id in hex, joined by `:`, fit.

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

An unknown series is `NotFound`; a range of more than 7 days is `Invalid`; an unknown key is `OK` with no
points and a total of 0.

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
| 3 | NotFound | the named user, device, Package, Entry, object, pin, peer, pairing, operation, series or address does not exist here |
| 4 | AlreadyExists | an identity, static peer, subscription id, session or path already exists |
| 5 | Invalid | a value is in bounds but wrong in substance; `Error.detail` names the field |
| 6 | NoIdentity | the request needs an identity and the device has none |
| 7 | UserKeyAbsent | the request needs the user's private key, which is on another device |
| 8 | NotOwner | the request needs the Package's owner |
| 9 | NotWritable | the own user has no `Write` right on the Package, or this device is removed |
| 10 | KindTaken | another connected service holds the kind |
| 11 | KindNotRegistered | the request needs a kind this service did not register |
| 12 | ServiceUnavailable | the service is not configured or not connected |
| 13 | ServiceTimeout | the service did not reply in time |
| 14 | Busy | a limit of §12 is reached: outstanding callbacks, subscriptions, operations, connections, one pairing |
| 15 | Draining | the session is Draining |
| 16 | Cancelled | the operation was cancelled |
| 17 | IoError | the file system refused: `Error.detail` carries the platform message |
| 18 | ClockExhausted | the next clock would not be well-formed (SPEC.md §8.6); the Package is read-only on this device |
| 19 | GapTooLarge | the replay would exceed 256 Entries; page with `ListStored` first |
| 20 to 32767 | (reserved) | for extensions of this protocol |
| 32768 to 65535 | (service-defined) | §11.2 |

### 11.2 Service-defined codes

A `Reply` to `ForwardedCall` MAY carry a status from 32 768 to 65 535, whose meaning the service's own document
states; the Host returns it to the caller unchanged with the service's `Error` record. A service MUST NOT use a
code below 32 768 in a `ForwardedCall` reply except those of §11.1 with their meaning here.

### 11.3 Violations

A violation is any of: a frame outside its bounds or not completed in time (§3.1); a message id in the session
range and unknown, in the reserved range 34 to 255, or 2048 and above; a frame of a class the sender may not
send (§3.3); a message in the wrong state (§4.1), a second `Hello` included; a field outside its stated bounds
or a malformed `string`, `bool` or `optional` in a request or reply; a `correlation` of 0, one that is still
outstanding, or a 65th outstanding request (§3.4); a `Reply` for no outstanding callback (§3.6); `Hello.role`
outside the registry. On a violation the Host MUST close the connection with `Bye(Violation)` and SHOULD log
the connecting process. Everything else that is wrong with a request is an error response, and the connection
continues.

A Host defect seen by a client (a `Response`, `Event` or `Callback` outside its bounds, a `Response` for no
outstanding request, an event on no live subscription) is handled by the client as a lost connection: it
closes and reconnects (§2.3).

## 12. Limits

Every limit is a bound the Host MUST enforce and a client MUST respect.

| Limit | Value | On breach |
|---|---|---|
| frame length | 2 to 4 198 400 bytes | violation |
| frame completion | 30 s from the first byte | violation |
| `Hello` after connecting | 10 s | close |
| `Pong` after `Ping` | 30 s; `Ping` after 60 s without receiving | `Bye(Unresponsive)` |
| outstanding requests per connection | 64 | violation |
| outstanding callbacks per connection | 64 | queue / `Busy` (§9.8) |
| subscriptions per connection | 64 | `Busy` |
| queued events per subscription | 256 | lapse (§7.4) |
| replay per `Subscribe` | 256 Entries | `GapTooLarge` |
| frames waiting to be written, per connection | 64 MiB, or 30 s for one frame | `Bye(Unresponsive)` |
| running operations per connection | 16 | `Busy` |
| ended operations kept per connection | 64 | oldest dropped |
| user connections | 64 | `Bye(Refused, "busy")` |
| service connections | 1 per configured name | `Bye(Duplicate)` |
| pairings per Host | 1 at a time, 10 minutes | `Busy` / `Failed` |
| `Drain` grace | 5 000 ms default, 30 000 ms at most | `Bye(Shutdown)` |
| `ServiceCall` / `ForwardedCall` | 60 s | `ServiceTimeout` |
| `ValidatePayload` | 10 s, then held and asked again | held |
| page of `ListHistory`, `ListStored` | 64 Entries, cut to the frame | — |
| page of `ListPackages`, `ListPins` | 256 | — |
| points per `QueryTelemetry` | 10 080 (7 days) | `Invalid` |
| telemetry retention | 7 days of minutes; totals for ever | dropped |

## 13. Security considerations

- **Authentication is the operating system's.** The pipe admits the OS user the Host serves and nobody else
  (§2.2). That user may do everything the user role may do anyway, on the same machine, with the same files;
  the protocol adds no finer right, and claims none. Where the Host runs under another account than the user
  (a Windows service), the pipe's access list is the only boundary, and `PIPE-ACCESS.md` (§2.4) is where it is
  specified.
- **The service role is configuration, not authentication.** A program becomes a service by claiming a name
  the Host's configuration lists (§4.2). Any program of the same OS user can claim it; the role exists so that
  two services do not hold one kind at once and so that a stray program does not register a kind by accident,
  not to keep a hostile local program out. A hostile program of the same user could do everything a service
  can; that is the threat model of DESIGN.md §14 ("a malicious local program").
- **No key crosses the pipe.** Signing happens in the Host; a service hands over bytes and gets back a signed
  Entry. The user key's backup is the one exception and is specified apart (§6.1). A `Response` never carries a
  private key, and `GetObject` of a Roster returns public keys only.
- **Paths are the Host's.** `Materialise`, `PutFile` and `GetStorePath` name paths the Host resolves with its
  own permissions. A client can make the Host read any file the Host's account can read and write anywhere it
  can write; with a per-user Host that is the user's own power, and with a Host under another account it is
  what §2.4's document bounds. The Host MUST NOT follow a symlink inside a path it materialises into out of the
  target directory (SPEC.md §8.4's escape rule applies to the destination as a whole).
- **Everything a client sees is verified.** Records are derived from objects the Host verified under SPEC.md;
  `GetObject` returns stored objects only; direct reads see files that are complete and verified. A client is
  never handed a held object.
- **Payload validation trusts the service** (§9.8): an `Invalid` reply drops a peer's Entry. A faulty service can
  therefore make this device drop Entries other devices keep, which affects this device's view only, since
  SPEC.md's validity does not depend on payloads; the Entry is fetched again when the service is fixed and the
  peer lists the Head.
- **Bounded by construction.** Frames, lists, outstanding requests and callbacks, subscriptions, queues,
  operations and connections are bounded (§12), and the Host never blocks on a client (§7.4). A client that
  stops reading loses its connection, not the mesh.
- **Telemetry stays on the device** (§10.2). It names peers, Packages and amounts; it is readable by every local
  program of the user, like everything else over the pipe.
- **Process identity is logged, not enforced** (§2.2). The connecting process id tells a log which program did
  what; it is not used to grant anything, because the operating system can give an id a different meaning than
  the Host assumes.

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
| 517 | AddTelemetry | Request (service) | 9.10 |
| 518 | Emit | Request (service) | 9.9 |
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
| 1536 | ValidatePayload | Callback | 9.8 |
| 1537 | ForwardedCall | Callback | 9.8 |

Ranges (§3.3): 7 to 31 session extensions (a violation when unknown); 34 to 255 reserved (a violation);
259 to 271, 278 to 287, 290 to 303, 320 to 351, 357 to 367, 375 to 383, 386 to 399, 402 to 415, 419 to 431 and
436 to 511 user-request extensions, grouped as their neighbours; 519 to 767 service-request extensions; 768 to
1023 request extensions of either role (all answered `UnknownRequest` when unknown); 1035 to 1535 event
extensions (ignored when unknown); 1538 to 2047 callback extensions (answered `UnknownRequest` when unknown);
2048 to 65535 never assigned (a violation).

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
| 0 | the store directory is readable by clients; `GetStorePath` succeeds (§9.7) |
| 1 | the Host runs under another OS account than the client (§13); paths are the Host's |
| 2 | the Host keeps telemetry (§10); clear on a Host configured without it, and then §10's requests are `NotFound` |
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
| 1 | Head | the Package id | the Host (SPEC.md §11.2) |
| 2 | Policy | the Package id | the Host (SPEC.md §11.2) |
| 3 | User | all zero | the user role (§6.5) |
| 4 | Service | `H(service name)` | the service role (§9.6) |

5 to 255 reserved.

### 14.9 Status codes

The table of §11.1: 0 to 19 assigned, 20 to 32 767 reserved, 32 768 to 65 535 service-defined.

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

### 14.12 Package change kinds

| Value | Change | `PackageChanged` carries |
|---|---|---|
| 1 | Followed | created here, or started following |
| 2 | Manifest | a new current Manifest |
| 3 | Grant | a new current Grant, in `grant` |
| 4 | Head | a new or changed Head, in `head` |
| 5 | Policy | a new local policy |
| 6 | Unfollowed | stopped following; the record shows `followed` false |

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
| `pins.bytes` | gauge | the pin reason name: `Head`, `Policy`, `User`, `Service` |
| `sessions.open` | gauge | `in` or `out` |
| `admission.refused` | counter | the port: `mesh`, `outbound`, `local` |
| `discovery.seen` | counter | `mdns`, `broadcast`, `static` |
| `local.requests` | counter | the client's `Hello.name` |
| `local.violations` | counter | the client's `Hello.name` |
| `edge.crossings.in`, `edge.crossings.out` | counter | `<link id>:<Package id>`; reserved for the Edge (DESIGN.md §16) |
| `svc.<service>.<name>` | counter | the service's choice (§9.10) |

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
class rules of §3.3 against a frame of each class from each side; `Hello` refused for each reason of §4.2 and
`Duplicate` against a live service name; a `WriteEntry` with no parents producing an Entry that verifies under
SPEC.md §8.6, orders under §9.3 and is faultless under §8.6 against an in-memory node, for the three parent
cases of §9.4; a `ValidatePayload` that is answered `Invalid` leaving nothing stored; the replay rule of §7.2
delivering exactly the Entries with `storeSeq` ≥ `fromSeq` once and in order, before and across a lapse of
§7.4; the slow-consumer rule leaving other subscriptions and requests intact; an operation cancelled by
`CancelOperation`, by `Drain` and by a closed connection; direct reads of §9.7 against an object that is then
evicted; and the pipe admitted for the Host's OS user and refused for another on each desktop platform. Test
vectors are published with the reference implementation and are part of this specification once they exist.

## Appendix A. Layouts at a glance

```
frame          := length:u32 messageId:u16 payload
Response       := correlation:u32 status:u16 body;   Reply := callback:u32 status:u16 body
Request        := correlation:u32 body;              Event := subscription:u32 body;   Callback := callback:u32 body

Hello          := version:u16 role:u8 name:string clientVersion:string capabilities:u32
HelloAck       := version:u16 hostVersion:string role:u8 capabilities:u32 identity:Identity storeSeq:u64
Bye            := reason:u8 detail:string;   Ping | Pong := nonce:u64;   Drain := grace:u32

Identity       := present:bool userId:id userName:string userKeyPresent:bool deviceId:id deviceName:string rosterSeq:u64 created:time
User           := userId:id name:string self:bool knownSince:time rosterSeq:u64 devices:u16
Device         := deviceId:id userId:id name:string addedAt:time removed:bool self:bool lastSeen:time
Package        := packageId:id ownerId:id kind:u16 name:string created:time retention:u8 manifestSeq:u64 manifestHash:hash
                  branches:list<Branch> followed:bool policy:u8 rights:u8 heads:list<Head> entries:u64 latestClock:u64
Grant          := hash:hash packageId:id granteeId:id rights:u8 seq:u64
Entry          := hash:hash storeSeq:u64 stored:time packageId:id deviceId:id clock:u64 parents:list<hash> kind:u16 payload:blob
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
ListStored     := kinds:list<u16> fromSeq:u64 limit:u16 → entries:list<Entry> more:bool
ListPins       := hash:optional<hash> packageId:optional<id> after:optional<PinKey> → pins:list<Pin> more:bool
Pin            := hash:hash                           → pin:Pin;   Unpin := hash:hash → -
GetStore       := -                                   → store:Store;   SetStoreBudget := bytes:u64 → store:Store
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

RegisterKind   := kind:u16 entryKinds:list<u16>       → storeSeq:u64 packages:u32
WriteEntry     := packageId:id kind:u16 parents:list<hash> payload:blob → entry:Entry
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

ValidatePayload := packageId:id deviceId:id clock:u64 kind:u16 payload:blob → -
ForwardedCall  := client:u32 clientName:name method:name payload:blob → payload:blob
```

## Appendix B. Worked example

Alice's laptop runs the Host; its device is L, its store sequence stands at 4 120, and it follows the Channel
`C` of SPEC.md Appendix B, whose newest Entries are `e7` (L's own Head, clock 7) and `e8` (from Alice's phone P,
clock 8). The Chat service is in-process; the app is a separate process on the pipe.

1. **Chat connects.** Over its in-memory pair Chat sends `Hello(1, role 2, "chat", "1.0", 0)`. The configuration
   lists `chat`, no other `chat` connection is live, so the Host answers `HelloAck(1, "1.0", 2, flags 0b101,
   identity{present, Alice, L, userKeyPresent true}, storeSeq 4120)`. Chat sends `RegisterKind(kind 2, [2, 3])`
   with correlation 1 and gets `Response(1, OK, storeSeq 4120, packages 3)`. Its own index ends at store
   sequence 4 100, so it sends `Subscribe(subscription 1, entries, filter{kinds [2, 3], fromSeq 4101})`
   (correlation 2) and `Subscribe(2, packages, filter{kinds [2]})` (correlation 3). The Host answers both,
   then replays twenty `EntryStored` events on subscription 1, 4 101 to 4 120, in order; Chat indexes them.
2. **The app connects.** Over the named pipe the app sends `Hello(1, role 1, "app", "1.0", 0b1)` and gets its
   `HelloAck`. It sends `GetIdentity`, `ListPackages(followedOnly)`, `Subscribe(1, packages, filter{})` and
   `Subscribe(2, entries, filter{packageId C})` with correlations 1 to 4, all outstanding at once; the responses
   come back in whatever order the Host finishes them. The app then asks Chat for the rendered timeline with
   `ServiceCall("chat", "timeline", {C, last 50})`, which reaches Chat as `ForwardedCall(callback 1, client 7,
   "app", "timeline", …)`; Chat answers from its index with `Reply(1, OK, payload)` and the app gets the
   `Response`.
3. **Alice types.** The app sends `ServiceCall("chat", "send", {C, "hello"})` (correlation 5). Chat gets
   `ForwardedCall(2, …)` and sends `WriteEntry(C, kind 2, parents [], payload Message{"hello"})` (correlation
   4). The Host chooses `clock` 9 (the highest it holds is 8), parent `e7` (L's own Head), signs with L's device
   key, stores `e9` with store sequence 4 121, moves L's Head to `e9`, pins it with reason `Head`, and sends
   `PackageChanged` and `Have` to P and to Bob's desktop D. It answers Chat `Response(4, OK, Entry e9)`, and
   delivers `EntryStored(e9)` on Chat's subscription 1 and on the app's subscription 2, and `PackageChanged(Head)`
   on Chat's subscription 2 and the app's subscription 1. Chat indexes `e9` and replies `Reply(2, OK, {e9})`;
   the app's `Response(5, OK, …)` follows. The app already drew the message from its own `EntryStored`.
4. **Phone answers.** P writes `e10` (clock 10, parent `e8`) and the Host receives it over AMP/1. It verifies the
   signature against Alice's Roster, holds `e8` already, and because kind 2 is registered sends
   `ValidatePayload(callback 3, C, P, 10, 2, payload)` to Chat. Chat parses the `Message` and replies
   `Reply(3, OK)`; the Host stores `e10` as 4 122 and both subscribers get `EntryStored(e10)`. Had Chat replied
   `Invalid`, `e10` would have been dropped and P recorded as faulty for it, and nothing delivered.
5. **An attachment.** The app opens an attachment Tree `t` named in `e10` that the phone policy did not fetch
   here either; it sends `Fetch(C, t, recursive true, pin false)` and gets operation 1. `OperationChanged`
   events on a subscription to `operations` show `done` climbing in objects; the final one is `Done` with
   `{objects 5, bytes 3 145 728}`. The app reads the ChunkLists and Chunks with `GetObject` and renders them.
6. **A slow app.** The app's window is minimised and it stops reading its pipe while the laptop syncs a large
   Archive; subscription 2 reaches 256 queued `EntryStored`s for a different Package, so the Host drops the
   queue, ends subscription 2 and queues `SubscriptionLapsed(dropped 257, lastSeq 4 380)`. When the app reads
   again it sees the lapse, sends `Subscribe(2, entries, filter{packageId C, fromSeq 4381})` and is current
   again; its subscription 1 and the connection were never affected.
7. **Shutdown.** The Host stops: it sends `Drain(5000)` to the app and to Chat. The app sends `Bye(Done)` at
   once. Chat finishes writing its index, sends `Bye(Done)`, and the Host's `RegisterKind` for kind 2 is
   released. A connection that had not answered after five seconds would have got `Bye(Shutdown)`.
