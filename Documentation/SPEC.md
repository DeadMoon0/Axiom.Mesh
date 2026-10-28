# Axiom Mesh Protocol, Version 1 (AMP/1)

**Status:** Draft 2, 2026-10-28, after an adversarial review of draft 1 (36 findings, all addressed or recorded
in §13). Normative for the first implementation. Where this document and [DESIGN.md](DESIGN.md) differ, this
document wins; DESIGN.md explains why things are as they are, this document says exactly what they are.

**Abstract.** AMP/1 is the protocol two nodes of an Axiom Mesh speak over one TLS connection: how they
identify and authenticate each other, how data is named, encoded, signed and verified, how a node learns what
its peer holds and fetches what it lacks, and how it stores what it fetched. The unit of data is the
**object**, named by its SHA-256; the unit of sharing is the **Package**, a signed history of objects; the
protocol is symmetric, and every byte a node accepts is verifiable without trusting the peer that sent it.

## Table of contents

1. Introduction
2. Cryptographic primitives and identifiers
3. Discovery
4. Connection
5. Framing and encoding
6. Session
7. Messages
8. Objects
9. Packages
10. Replication
11. Local data handling
12. Errors and violations
13. Security considerations
14. Registries
15. Conformance
Appendix A. Message and object layouts at a glance
Appendix B. Worked example

## 1. Introduction

### 1.1 Requirements language

The key words MUST, MUST NOT, REQUIRED, SHALL, SHOULD, SHOULD NOT, RECOMMENDED, MAY and OPTIONAL are to be
interpreted as described in RFC 2119 and RFC 8174 when, and only when, they appear in all capitals.

### 1.2 Terminology

| Term | Meaning |
|---|---|
| **Node** | One running instance of the protocol on one device. |
| **User** | A person, identified by a long-lived signing key (§2.3). |
| **Device** | One installation, owned by one user, identified by its own signing key (§2.3). |
| **Peer** | The node at the other end of a connection. |
| **Object** | A byte string transferred as a unit, named by its hash (§8). |
| **Package** | A signed history of objects with one owner (§9). |
| **Entry** | One signed step in a Package's history (§8.6). |
| **Head** | The newest entry one device wrote to a Package (§9.3). |
| **Roster** | A user's signed list of devices (§8.8). |
| **Grant** | An owner's signed statement that a user may read or write a Package (§8.9). |
| **Known user** | A user whose id this node trusts: through pairing (§13), or as the owner named by a verified Manifest, or as the grantee named by a verified Grant, of a Package the node follows. |
| **Followed Package** | A Package the node has chosen to replicate: every Package its own user owns, and granted Packages by device policy or the user's choice. A node ignores `PackageState`s, `Have`s and `PackageChanged`s for Packages it does not follow, except to record that the peer lists them. |
| **Held** | In the bounded buffer of objects received but not yet verified (§10.6): never served, never pinned, never acted on. |
| **Stored** | In the object store, which only verified objects enter (§11.1). |
| **Established session** | A session on which both sides have received the other's `Hello` (§6.2). |

### 1.3 Notation

Byte layouts are written as `name: type` lists in wire order. Types are defined in §5.2. All multi-byte
integers are big-endian. Sizes use binary units: 1 KiB = 1024 bytes, 1 MiB = 1024 KiB. Hex is lowercase.

### 1.4 Versioning

This is protocol version 1. The version is carried in the TLS ALPN (`axiom-mesh/1`), in `Hello` (§7.1) and in
the second byte of every object (§8.1). A node MUST refuse a peer whose major version differs, by not
offering the ALPN.

Within version 1, extensions MAY add message ids 17 to 255, object tags 8 to 255, entry kinds, Package kinds
and capability flags from the reserved ranges of §14. So that a 1.0 node and a 1.x node interoperate:

- a frame whose message id is 17 to 255 and unknown to the receiver MUST be ignored (read and discarded);
  an unknown id 0 to 16 is a violation (§12.1);
- an object whose tag is unknown is **opaque**: it is verified by its hash only, stored, replicated and
  pinned by the rules of whatever references it, and never interpreted;
- an unknown entry kind or Package kind is carried opaquely (§8.5, §9.2);
- an unknown capability flag is ignored.

Anything that changes the meaning of an existing byte is version 2.

## 2. Cryptographic primitives and identifiers

### 2.1 Hash

`H(x)` is SHA-256 of `x`, 32 bytes. Every object name, user id, device id and Package id is an output of `H`.
Collision resistance of SHA-256 is assumed throughout.

### 2.2 Signatures

Signatures are ECDSA over the NIST P-256 curve with SHA-256, encoded as the 64-byte IEEE P1363 fixed form
`r ‖ s`, each 32 bytes big-endian. A signature MUST satisfy `1 ≤ r < n` and `1 ≤ s ≤ (n−1)/2` (low-s), `n`
being the curve order; a signed object whose signature is outside these bounds is malformed (§8.1). Signers
MUST produce low-s signatures and SHOULD use deterministic nonces (RFC 6979), so that one signer signing the
same bytes twice produces the same object.

A signature over a structure is computed over `H(domain ‖ body)`, where `domain` is the structure's domain
string from §14.5 encoded as UTF-8 without a terminator, and `body` is every byte of the structure that
precedes the signature field, exactly as encoded on the wire. A verifier MUST hash the bytes it received,
never a re-encoding of its own.

### 2.3 Keys and identifiers

A key pair is an ECDSA P-256 key pair. A public key is encoded on the wire as the 65-byte uncompressed point
`0x04 ‖ X ‖ Y` (`pubkey` in §5.2); a `pubkey` whose first byte is not `0x04` or whose point is not on the
curve is malformed.

| Identifier | Definition |
|---|---|
| User id | `H(user public key)` over the 65-byte encoding |
| Device id | `H(device public key)` over the 65-byte encoding |
| Package id | `H("axiom-mesh/package/1" ‖ owner user id ‖ nonce)`, `nonce` 32 random bytes chosen at creation and carried in the Manifest (§8.7), so every receiver can check the derivation |

Ids are 32 bytes and never change. A key MUST be used as a user key or as a device key, never both; a roster
device whose id equals a known user id, or a user id that equals a device id in a held roster, is treated as
malformed where it appears.

### 2.4 Certificates

Each device holds a self-signed X.509 certificate whose subject public key is the device key. The
certificate MUST carry the Digital Signature key usage and both the server and client authentication extended
key usages. Validity SHOULD be ten years; a node SHOULD renew its certificate before expiry so that TLS
stacks that check validity by default do not refuse it, and peers MUST disable that check (§4.3) because they
pin the key, not the certificate. No other field carries meaning. The subject name SHOULD be
`CN=<device id, hex>` for logs.

### 2.5 Randomness

Nonces and keys MUST come from a cryptographically secure random source.

## 3. Discovery

Discovery answers which addresses on the local network claim to run a node and which device id each claims.
A claim is a hint, never trusted: only the TLS handshake (§4.3) proves it. A node MUST NOT dial a discovered
address whose claimed device id is not, in a roster it holds, a device of a known user (its own included).

### 3.1 mDNS / DNS-SD

A node SHOULD announce the service `_axiom-mesh._tcp` with the instance name equal to the first 32 hex
characters of its device id (a DNS-SD instance name is at most 63 bytes), the port of its mesh listener, and
these TXT records:

| Key | Value |
|---|---|
| `v` | `1` |
| `d` | device id, hex, 64 characters |
| `u` | user id, hex, 64 characters |

Unknown keys MUST be ignored. A record whose `v` is not `1`, or whose `d` does not start with the instance
name, MUST be ignored.

### 3.2 UDP broadcast

Where multicast is filtered, a node MAY broadcast on UDP port 41811 and MAY listen on it. The datagram is:

```
magic:      bytes<4>   = "AMD1"
flags:      u8         bit 0: 1 = announcement, 0 = query; bits 1 to 7 MUST be 0
deviceId:   id
userId:     id
port:       u16        the TCP port of the mesh listener
```

A query carries the sender's own ids and port and asks every listener to answer with an announcement, sent
unicast to the query's source address. A node MUST NOT send more than one announcement per second per
interface and MUST ignore datagrams of any other length, magic or flag bits. Nothing in the datagram is
authenticated; it is a hint like a DNS-SD record.

### 3.3 Static peers

A node MAY hold a list of `host:port` addresses to dial on start and on a schedule. The TLS handshake
decides whether the node there is acceptable.

## 4. Connection

### 4.1 Transport

A connection is one TCP connection. A node listens on one TCP port, default 41810, on every interface it
serves. TCP keep-alive SHOULD be enabled; Nagle's algorithm MUST be disabled.

### 4.2 TLS

Every connection is TLS 1.3 (RFC 8446). Older versions MUST NOT be offered or accepted. Both sides present a
certificate (§2.4); a handshake in which either side presents none MUST fail. Cipher suites MUST be
forward-secret AEAD suites; implementations MUST support `TLS_AES_128_GCM_SHA256` and SHOULD support
`TLS_AES_256_GCM_SHA384` and `TLS_CHACHA20_POLY1305_SHA256`. The ALPN MUST be negotiated; a connection
without a negotiated ALPN MUST be closed. ALPNs are listed in §14.4.

Early data (0-RTT) MUST NOT be offered or accepted. Session tickets MUST NOT be issued, so every connection
runs a full handshake and §4.3 is evaluated against the rosters current at that moment. Renegotiation is not
used.

The TLS handshake MUST complete within 10 seconds of the TCP connection, or the connection is closed.

### 4.3 Peer validation

Each side validates the other's certificate by the public key alone:

1. Let `K` be the peer certificate's subject public key and `D = H(K)` (§2.3).
2. `D` MUST be a device that is not removed in the current roster (§8.8) of a known user (§1.2), this node's
   own user included.
3. Nothing else is checked: no certificate chain, no name, no revocation, no validity period. An
   implementation MUST configure its TLS stack to skip those checks.

A failed check MUST close the connection without sending a protocol byte. The accepting side can do this
before its handshake completes; the dialling side, by the nature of TLS 1.3, may only learn of its own
rejection as a closed connection after its handshake completed (§6.2).

### 4.4 Dialling

A node dials a peer when discovery or a static address names an acceptable device (§3) and no established
session to that device exists. A node SHOULD NOT dial the same address more often than once per 10 seconds
after a failure, with exponential backoff to at most 5 minutes. Admission limits (§4.6) MUST be decided
before the TCP connection is opened.

### 4.5 Duplicate connections

A node MUST hold at most one established session per device id. When a second session to the same device
becomes established, or is about to, the node decides which to keep, and both nodes reach the same decision
without talking:

1. If the old session has received nothing for 60 seconds, it is dead: keep the new, close the old.
2. Otherwise, if both sessions were dialled by the same device, keep the newer.
3. Otherwise keep the session dialled by the device with the lower device id (compared as 32-byte unsigned
   big-endian integers).

The session not kept is closed with `Bye(reason = Duplicate)` (§7.13).

### 4.6 Admission

A node MUST bound the connections it serves: in total, per peer address, and per second. The defaults are
those of the host the implementation runs on (see DESIGN.md §10.2); a mesh node SHOULD set the per-address
limit to a small number such as 4, since a device needs one session. Admission applies to accepted and
dialled connections alike, and for dialled ones is decided before the dial.

## 5. Framing and encoding

### 5.1 Frames

Every protocol message is one frame:

```
length:     u32        number of bytes that follow, i.e. 2 + len(payload)
messageId:  u16        from the registry in §14.1
payload:    bytes      the message body, len = length - 2
```

`length` MUST be at least 2 and at most 4 198 400 (4 MiB + 4 KiB). A frame outside these bounds is a
violation (§12.1). A receiver MUST NOT allocate more than `length` bytes for a frame before it has received
them; it MAY stream a frame's payload. A frame MUST be received completely within 30 seconds of its first
byte, else the connection is closed with `Bye(Violation)`.

### 5.2 Types

| Type | Encoding |
|---|---|
| `u8`, `u16`, `u32`, `u64` | unsigned, big-endian, fixed width |
| `bool` | `u8`, 0 or 1; any other value is malformed |
| `bytes<n>` | exactly n bytes |
| `hash`, `id` | `bytes<32>` |
| `pubkey` | `bytes<65>`, `0x04 ‖ X ‖ Y` |
| `signature` | `bytes<64>`, `r ‖ s` |
| `string(max)` | `u16` byte length, then that many bytes of UTF-8, at most `max` bytes, no NUL, valid UTF-8 |
| `blob(max)` | `u32` byte length, then that many bytes, at most `max` |
| `list<T>(max)` | `u16` count, then that many `T`, at most `max` |
| `biglist<T>(max)` | `u32` count, then that many `T`, at most `max` |

A receiver MUST detect a length or count over its maximum from the length field before reading the content.
In a **message**, a value outside its bounds is a violation (§12.1). In an **object**, it makes the object
malformed (§8.1, §12.2). An enumerated field (`mode`, `rights`, `retention`, `kind`, `reason`) with a value
this document does not define is handled as its section says; where a section says nothing, the value is
malformed in an object and a violation in a message.

### 5.3 Canonical form

Every structure in this document has exactly one valid encoding: fixed field order, no optional fields, no
padding, and every list in the order its definition states. Lists that are hashed or signed state an order
in §8; a list out of order makes the object malformed. Two encoders given the same values produce the same
bytes, which, with deterministic signatures (§2.2), makes hashing and signing well defined.

## 6. Session

### 6.1 States

```
          send Hello, receive Hello
Connected ─────────────────────────► Established ──ServiceOpen/Accepted──► Switched
    │                                     │                                    │
    └──── Bye or close ◄──────────────────┴──────────── service close ◄────────┘
```

- **Connected:** the TLS handshake completed with ALPN `axiom-mesh/1`. Each side MUST send `Hello` as its
  first frame, immediately, without waiting for the peer's. Before the peer's `Hello`, a node accepts only
  `Hello` and `Bye`; any other frame is a violation. If no `Hello` arrives within 10 seconds, the connection
  is closed.
- **Established:** both `Hello`s were received and validated (§7.1). Every message of §7 may flow in either
  direction at any time. The protocol is symmetric: there is no client and no server above TLS.
- **Switched:** the connection was handed to a service protocol (§7.10). No AMP/1 frame is exchanged, `Bye`
  included, until the service protocol ends by its own rules and the session returns to Established.
  Liveness (§6.3) is suspended; it is the service protocol's concern.

`Bye` is accepted in Connected and Established; a malformed `Bye` is treated as a close.

### 6.2 Establishment and refusal

A session counts as established for a node only once it has received and validated the peer's `Hello`. A
node MUST NOT treat the completion of its own TLS handshake as acceptance by the peer (§4.3). A connection
that closes before the peer's `Hello` arrived MUST be recorded as a refusal of this node by that peer, and
the node SHOULD back off from that peer as for a failed dial (§4.4).

### 6.3 Liveness

In Connected (after its own `Hello`) and Established, a node MUST send `Ping` when it has sent nothing for
60 seconds and MUST answer `Ping` with `Pong` carrying the same nonce. A session on which nothing was
received for 5 minutes MUST be closed with `Bye(Idle)`. A `Pong` with an unknown nonce is ignored.

### 6.4 Close

An orderly close is `Bye` followed by TLS close-notify and the TCP close. A node that receives `Bye` MUST
NOT send further frames and SHOULD close. A connection may also simply end; a node MUST handle both.

## 7. Messages

Payload layouts; the message id is in the frame header (§5.1, §14.1). "Established only" means a message
received in any other state is a violation.

### 7.1 `Hello` (1)

```
version:        u16                 = 1
deviceId:       id
userId:         id
rosterSeq:      u64                 the sequence number of the sender's user's roster, as the sender holds it
capabilities:   u32                 bit flags, §14.3
services:       list<string(64)>(64) service protocol names the sender offers, §14.4
```

On receipt a node MUST check: `version` is 1; `deviceId` equals `H(K)` for the peer's TLS public key `K`
(§4.3); `userId` is a known user; `deviceId` is a device of `userId`, not removed, in the current roster the
node holds for that user. A failing check is a violation and the connection is closed with
`Bye(Unauthorized)`.

Then, if the peer's `rosterSeq` is lower than the sequence number of the roster this node holds for that
user, the node MUST send that Roster object as an unsolicited `Object` (§7.6) immediately after its own
`Hello`. If it is higher, the node MUST send `RosterQuery(userId)` (§7.9).

### 7.2 `Packages` (2), Established only

```
page:       u16                 0-based page number
last:       bool                true on the final page
packages:   list<PackageState>(64)
```

```
PackageState:
  packageId:      id
  manifestHash:   hash            the current Manifest the sender holds (§8.7)
  manifestSeq:    u64
  grants:         list<hash>(256) current Grant objects for this Package, §9.7 says which; ascending
  heads:          list<Head>(256) ascending by deviceId
Head:
  deviceId:       id
  entryHash:      hash
  clock:          u64
```

A node MUST send a complete listing once after establishment, and again whenever the *set* of Packages it
may list to this peer changes. The listing MUST contain exactly the Packages for which the peer's user is the
owner or holds a current `Read` or `Write` Grant (§8.9), and no other. Rules for a listing:

- Pages are numbered from 0 consecutively; a page out of sequence is a violation.
- A listing MUST hold at most 65 536 `PackageState`s in all and MUST complete within 60 seconds of page 0;
  more, or later, is a violation.
- Between the pages of one listing the sender MUST NOT send another `Packages` or `PackageChanged`; other
  messages MAY interleave. A `Packages` with `page = 0` while a listing is incomplete discards the partial
  listing and starts over.
- A page MUST be cut so that its frame stays within §5.1; the list maximum of 64 is a bound on count, not a
  guarantee of fit.
- The receiver replaces its view of the sender's state when `last` is true.

A node whose user is not the owner and that receives a listing from a device of the owner which omits a
Package the same device listed before MUST treat its own Grant for that Package as withdrawn until a newer
Grant proves otherwise.

### 7.3 `PackageChanged` (3), Established only

```
state:      PackageState
```

Sent to every established peer that may see the Package whenever the state the node would list for that
Package changed: a new current Manifest or Grant, or a Head it stored, whether from a local write or from a
received object, except to the peer the change came from. Also sent when a Grant now lets the peer see a
Package it could not see before. A receiver MUST treat it as an update to its view from `Packages`. A
`PackageChanged` MUST NOT be sent while a listing (§7.2) is incomplete.

### 7.4 `Have` (4), Established only

```
hashes:     list<hash>(1024)
```

"I hold these objects." A node MAY send `Have` unsolicited when it stored new objects of a Package the peer
may see, and SHOULD send it for hashes it holds but has deferred answering (§7.6). A node MUST NOT claim an
object it does not hold or that the peer may not see (§9.7).

A receiver records a `Have` only for hashes it wants or may want (heads, parents and payload objects of
Packages it follows); every other hash is ignored. A node MUST bound the sources it records to 65 536 hashes
per peer, dropping the oldest.

### 7.5 `Want` (5), Established only

```
hashes:     list<hash>(64)
```

"Send these if you have them." The receiver answers each hash with `Object` or `NotFound`, in any order, and
MUST answer every hash eventually unless a `Cancel` for it arrives first. A node MUST NOT have more than 64
hashes outstanding (wanted, not yet answered or cancelled) toward one peer; a `Want` that would exceed that
on the receiving side is a violation.

### 7.6 `Object` (6), Established only

```
hash:       hash
object:     blob(4194304)       the object's bytes, §8
```

`hash` MUST equal `H(object)`; a mismatch is a violation. A receiver MUST verify the hash and the object's
well-formedness (§8) before storing, holding or forwarding it, and MUST NOT act on an object before that. An
`Object` the receiver did not want MAY be dropped, except a Roster after `Hello` or in answer to
`RosterQuery`, which MUST be processed; a node MUST NOT send unsolicited objects other than those.

A sender MUST NOT have more than 8 MiB of `Object` payload queued toward one peer. Past that it continues to
read and parse frames (so `Ping`, `Pong` and `Cancel` are still processed) but defers answering further
`Want`s until the queue drains. The 64-outstanding bound of §7.5 covers deferred wants too.

### 7.7 `NotFound` (7), Established only

```
hash:       hash
```

The answer to a `Want` the sender cannot satisfy, including an object the receiver may not see (§9.7): the
two cases are indistinguishable on the wire, deliberately.

### 7.8 `Cancel` (8), Established only

```
hashes:     list<hash>(1024)
```

The sender no longer wants these. The receiver MAY drop queued `Object`s for them and need not answer them.

### 7.9 `RosterQuery` (9) / `RosterNotFound` (16), Established only

```
RosterQuery:
  userId:     id
RosterNotFound:
  userId:     id
```

`RosterQuery` asks the peer for the current Roster it holds for that user; the peer answers with `Object`
carrying the Roster, or `RosterNotFound`, subject to §9.8.

### 7.10 `ServiceOpen` (10) / `ServiceAccepted` (11) / `ServiceRefused` (12), Established only

```
ServiceOpen:
  service:    string(64)          a service protocol name, §14.4
ServiceAccepted:
  service:    string(64)
ServiceRefused:
  service:    string(64)
  reason:     u8                  1 unknown, 2 busy, 3 not allowed; other values are read as 0, unspecified
```

`ServiceOpen` asks to switch the connection to the named service protocol. The receiver answers
`ServiceAccepted`, after which both sides speak the service protocol until it ends by its own rules and the
session returns to Established; or `ServiceRefused`. A node MUST NOT send any AMP/1 frame between its
`ServiceOpen` and the answer except `Ping`, `Pong` and `Bye`. A node MUST NOT send a second `ServiceOpen`
while its own is outstanding; that is a violation.

When a node with a `ServiceOpen` outstanding receives a `ServiceOpen` from the peer: if the peer's device id
is lower, the node treats its own request as refused with reason `busy` (no frame is sent for it) and
answers the peer's; if higher, it ignores the peer's `ServiceOpen`, which the peer withdraws by the same
rule. No frame crosses into the service stream.

### 7.11 `Ping` (13) / `Pong` (14)

```
nonce:      u64
```

Allowed in Connected after the node's own `Hello` was sent, and in Established. `Pong` echoes the nonce.

### 7.12 (reserved)

### 7.13 `Bye` (15)

```
reason:     u8                  §14.2; other values are read as 0, unspecified
detail:     string(256)         for logs; MUST NOT be interpreted
```

## 8. Objects

### 8.1 Common form

An object is a byte string:

```
tag:        u8                  §14.6
version:    u8                  = 1
body:       bytes               tag-specific, §8.2 to §8.9
```

Its name is `H(object)` over all of its bytes. An object MUST be at most 4 MiB. An object whose version is
not 1, or whose body does not parse exactly to its end (no trailing bytes), or has a field outside its
bounds (§5.2), is **malformed**; a malformed object MUST NOT be stored, held or forwarded, and its sender
SHOULD be recorded as faulty (§12.2). An object with an unknown tag is opaque (§1.4), not malformed.

Signed objects (Entry, Manifest, Roster, Grant) end in a `signature` field; §2.2 defines what it covers. A
signed object whose signature does not verify against the key its section names is **invalid**. When that
key is not yet known to the node, the object is **unverifiable**: it MAY be held (§10.6) until the key
arrives, and MUST NOT be stored, served, forwarded or acted on before it verifies.

### 8.2 `Chunk` (tag 1)

```
data:       bytes               the rest of the object, 1 to 1 048 576 bytes
```

File contents are cut into chunks of exactly 1 MiB except the last, which holds the remainder; a zero-length
file has no chunks. An implementation MAY use another chunking in a later version; a `ChunkList` is valid
whatever produced its chunks.

### 8.3 `ChunkList` (tag 2)

```
size:       u64                 total file size in bytes
chunks:     biglist<hash>(32768)
```

`size` is authoritative for the file's length and MUST equal the sum of the chunks' sizes; a receiver that
holds the chunks and finds otherwise records the ChunkList as faulty content (§12.2). The maximum file size
in AMP/1 is therefore 32 GiB.

### 8.4 `Tree` (tag 3)

```
entries:    list<TreeEntry>(8192)
TreeEntry:
  name:     string(255)
  mode:     u8                  1 file, 2 executable file, 3 directory, 4 symlink; other values: malformed
  hash:     hash                mode 1, 2: a ChunkList; 3: a Tree; 4: a Chunk holding the link target
  size:     u64                 modes 1, 2: the ChunkList's size; 3: MUST be 0 (else malformed); 4: the Chunk's length
```

Entries MUST be sorted by `name` as byte strings and MUST be unique; otherwise the Tree is malformed. A
`name` MUST NOT be empty, `.` or `..`, and MUST NOT contain `/`, `\` or NUL (malformed). `size` is a copy for
display and policy; the object it names is authoritative, and a mismatch, once both are held, is faulty
content handled like a merge conflict when materialised (§9.5, §11.4). An implementation MUST refuse to
write a tree to disk whose names, on that file system, would escape the tree's directory or collide
case-insensitively, and records that as a conflict too.

### 8.5 Entry payloads

An Entry's `payload` (§8.6) is a blob whose layout depends on `kind` (§14.7):

```
Version (kind 1):
  tree:         hash
  label:        string(64)       the branch name, "main" by default; MUST NOT be empty
  prerelease:   string(64)       empty, or a pre-release tag such as "beta.2"
  notes:        string(4096)
Message (kind 2):
  text:         string(16384)
  attachments:  list<Attachment>(64)
  replyTo:      hash             all zero when none
  Attachment:
    name:       string(255)
    hash:       hash             a Tree or ChunkList
    size:       u64              a copy, as in §8.4
Redaction (kind 3):
  target:       hash             the entry withdrawn
```

A payload of a known kind that does not parse exactly to its end makes the Entry malformed. An unknown
`kind` is not malformed: the Entry is stored, replicated and ordered, and its payload is opaque (a node
without the service for that kind still carries the Package).

### 8.6 `Entry` (tag 4)

```
packageId:  id
deviceId:   id                  the writing device
clock:      u64                 Lamport clock, §9.3
parents:    list<hash>(2)       entries this one follows, ascending as byte strings
kind:       u16                 §14.7
payload:    blob(1048576)       §8.5
signature:  signature           by the device key, domain "axiom-mesh/entry/1"
```

Well-formedness: `clock` MUST be below 2^63; `parents` MAY be empty only when `clock` is 0; otherwise
malformed.

An Entry is **valid** when, at the time it is first verified, its signature verifies against the public key
of `deviceId` in the current roster of a user who is the Package's owner or holds a current `Write` Grant,
and `deviceId` is not removed in that roster. Once an Entry is stored it stays valid: a later Roster or
Grant change affects only Entries not yet stored (§9.6). Once its parents are held, an Entry is faulty
content (§12.2) if any parent belongs to another `packageId`, if `clock` is not greater than every parent's
clock, or if `clock` exceeds the greatest parent clock by more than 2^32 (or exceeds 2^32 with no parents).

### 8.7 `Manifest` (tag 5)

```
packageId:  id
ownerId:    id
ownerKey:   pubkey              ownerId MUST equal H(ownerKey)
nonce:      bytes<32>           packageId MUST equal H("axiom-mesh/package/1" ‖ ownerId ‖ nonce)
kind:       u16                 the Package kind, §14.8
seq:        u64
name:       string(255)
created:    u64                 milliseconds since the Unix epoch; informational
retention:  u8                  0 default, 1 everything, 2 latest only; other values are read as 0
branches:   list<Branch>(64)    ascending by name; names unique
  Branch:
    name:   string(64)
    head:   hash                the entry the owner declares as the branch head; all zero when unset
signature:  signature           by ownerKey, domain "axiom-mesh/manifest/1"
```

A Manifest whose `ownerId` or `packageId` does not match its key and nonce is malformed. Because the id is
derived from the owner's id and a nonce only the Manifest carries, only the holder of `ownerKey` can
produce a Manifest for a given Package id. A Manifest is valid when its signature verifies against its own
`ownerKey`; it is trusted for a Package the node follows, which makes its owner a known user (§1.2).

A node MUST hold at most one current Manifest per Package: the valid one with the highest `seq`, ties
broken by the lower hash. `kind` MUST NOT change between Manifests of one Package; a Manifest with another
`kind` than the current one is faulty content and ignored.

### 8.8 `Roster` (tag 6)

```
userId:     id
userKey:    pubkey              userId MUST equal H(userKey)
seq:        u64
devices:    list<RosterDevice>(256)  ascending by deviceId; deviceIds unique
  RosterDevice:
    deviceId:   id              MUST equal H(key)
    key:        pubkey
    name:       string(64)
    addedAt:    u64             informational
    removed:    bool
signature:  signature           by userKey, domain "axiom-mesh/roster/1"
```

A Roster is valid when its signature verifies against its own `userKey`; a node stores a valid Roster only
for a known user (§1.2) and MUST NOT hold or store one for any other. A node holds at most one current
Roster per user: the valid one with the highest `seq`, ties broken by the lower hash.

Removal is permanent: a device that was `removed` in any Roster this node verified is treated as removed in
every later Roster of that user, whatever its flag; a user re-adds a device by giving it a new key. A later
Roster that omits a device is accepted and treated as if the device were marked removed, and SHOULD be
logged. A device id MUST appear in the current roster of at most one user this node holds; when a later
verified Roster conflicts with an earlier one, the later Roster is held but its conflicting device is
treated as removed, and the conflict is logged.

When a removal reaches a node: sessions with the removed device MUST be closed with `Bye(Unauthorized)`, no
new session is accepted from it, and Entries it signs that are not yet stored are invalid from then on.
Stored Entries stay (§8.6, §9.6).

### 8.9 `Grant` (tag 7)

```
packageId:  id
granteeId:  id                  a user id
granteeKey: pubkey              granteeId MUST equal H(granteeKey)
rights:     u8                  0 none (revocation), 1 read, 2 write (implies read); other values: malformed
seq:        u64
signature:  signature           by the owner's key, domain "axiom-mesh/grant/1"
```

A Grant is verified against `ownerKey` of the current Manifest of its Package, which the node MUST hold
first; it is unverifiable until then. A node holds at most one current Grant per (`packageId`,
`granteeId`): the valid one with the highest `seq`, ties broken by the lower hash. The owner needs no Grant.
A valid Grant of a followed Package makes its grantee a known user (§1.2).

## 9. Packages

### 9.1 Identity and ownership

A Package is identified by its `packageId` (§2.3) and owned by the user whose key is in its Manifest. The
owner is the only user who may sign Manifests and Grants for it. Ownership does not change in AMP/1.

### 9.2 Kinds

A Package's kind (§14.8) says what its Entries mean. Kind 1 `Archive` holds `Version` entries; kind 2
`Channel` holds `Message` and `Redaction` entries. A node MUST replicate, store and order Entries of any
kind; it interprets payloads only for kinds it has a service for.

### 9.3 Clocks, parents and heads

Every Entry carries a Lamport clock. A writing device sets `clock` to one more than the highest `clock` of
any Entry it holds for the Package, or 0 when it holds none. A device that holds any Entry of the Package
MUST name as `parents` its own current head (Channel) or the branch leaves it merges (Archive, §9.5); a
device that holds Entries but has no head of its own names the stored Entry highest in the total order. An
Entry with empty `parents` is allowed only from a device that holds nothing of the Package. A device whose
next clock would not be well-formed (§8.6) MUST stop writing to that Package and surface that to the user.

A Head is the newest Entry a device wrote; a Package has one Head per writing device. A node learns heads
from `Packages` and `PackageChanged` and MUST keep, per Package and per writing device, the stored Entry
with the highest `clock`, ties broken by the lower hash.

The **total order** of Entries in a Package is by `(clock, deviceId, hash)` ascending. Every node that holds
the same set of Entries computes the same order.

### 9.4 Channels

A Channel's visible order is the total order. There is no merge: a device appends with its own head as the
single parent. A `Redaction` applies to a `target` of the same `packageId` whenever both are stored,
whatever their order; a Redaction whose target is in another Package, or is itself a Redaction, is ignored.
It is honoured when its `deviceId` equals the target's `deviceId`, or when its `deviceId` is a device of the
owner; otherwise ignored. A node MUST keep a redacted Entry's bytes (they are history others verify) and MUST
NOT show its payload.

### 9.5 Archives, branches and merges

A `Version` names a branch by `label`. The **leaves** of a branch on a node are the stored `Version`s with
that label from which no other stored `Version` with that label descends. A branch's head for display is
the leaf highest in the total order. A node with more than one leaf MUST, when it next writes to that
branch, merge the two leaves lowest in the total order into one `Version` whose `parents` are both, and
repeat until one leaf remains, so that a chain of merges resolves any number of concurrent leaves with
`parents` of at most two.

The merge tree is derived path by path over the union of the two leaves' trees against the common
ancestor's tree, the common ancestor being the `Version` highest in the total order among those reachable
from both leaves (the empty tree if none):

| Changed on | Result |
|---|---|
| neither side | the ancestor's entry |
| one side only, including deleted on one side | that side's state |
| both sides, identically | that entry |
| both sides, differently | the entry from the side whose leaf is later in the total order, plus the other side's file (if it still has one) written beside it as `<name>.conflict-<first 8 hex of the other deviceId>` before any extension; a taken conflict name gets `-2`, `-3`, … appended |

A directory replaced by a file, or the reverse, counts as "changed differently". No content merge is
performed. The owner's Manifest `branches` list MAY name a head per branch; a node MUST treat it as the
owner's declaration for display and MUST NOT let it override the order above.

### 9.6 Who may write, and what stays valid

An Entry is valid only if signed by a device of the owner or of a `Write` grantee at the time it is first
verified (§8.6). Validity is then immutable: a stored Entry remains valid, forwardable and usable as a
parent after any Roster or Grant change, so that history walks (§10.3) and merge ancestors (§9.5) are the
same on every node. A Roster or Grant change affects only Entries not yet stored.

### 9.7 Visibility of Package objects

A node MAY list, claim (`Have`), or send (`Object`) an object of a Package to a peer only if the peer's user
is the owner or holds a current `Read` or `Write` Grant for that Package, with one exception: a Grant whose
`granteeId` is the peer's user is always visible to that peer, whatever its `rights`, so that a revocation
reaches the revoked user. `Packages.grants` lists to the owner's devices every current Grant, and to any
other peer only the Grant whose `granteeId` is that peer's user.

An object held under several Packages is visible if any of them is. Chunks, ChunkLists and Trees are visible
through the Packages whose Entries reference them; a node answering `Want` for a hash MUST resolve
visibility through the objects it knows reference that hash, and MUST answer `NotFound` when none that the
peer may see does.

### 9.8 Visibility of Rosters

A node serves a Roster (in answer to `RosterQuery` or after `Hello`) only for: the peer's own user, this
node's own user, and the owner and current grantees of Packages the peer may see. Any other `RosterQuery`
is answered `RosterNotFound`.

## 10. Replication

### 10.1 Goal

After enough time connected, two peers that may see the same Package hold the same Entries for it, and
each holds the objects its retention policy (§11.2) asks for. Replication is driven by state, not by
events: every `Packages` listing lets a node recompute what it wants.

### 10.2 On establishment

Each side sends `Packages` (§7.2). On receiving a `PackageState` for a Package it follows, a node compares:

1. **Manifest:** if `manifestSeq` is higher than its own, or equal with a lower `manifestHash`, it `Want`s
   `manifestHash`. A node that holds no Manifest for the Package `Want`s it first and does nothing else for
   the Package until it verifies.
2. **Grants:** it `Want`s every grant hash it does not hold.
3. **Heads:** for every Head whose `entryHash` it does not hold, it `Want`s the hash, but only if `deviceId`
   is, in the rosters it holds, a device of the owner or of a current `Write` grantee and not removed, and
   `clock` is above the clock of the Head it holds for that device.

For a Package it does not follow but is granted, a node that follows new Packages automatically (a device
policy, capability bit 0) MAY start following it by the same steps.

### 10.3 Walking history

On storing a valid Entry, a node `Want`s every parent it does not hold, until it reaches Entries it holds.
Then, for each new Entry, it `Want`s the objects the retention policy asks for: the Entry's payload objects
(a `Version`'s Tree, then the Tree's children, recursively; a `Message`'s attachments) as far as the policy
goes. A node MUST NOT `Want` an object it holds and SHOULD NOT `Want` the same object from two peers at
once except when a peer has not answered within 30 seconds.

### 10.4 Choosing a source

Wants are addressed to a peer that announced `Have` for the hash, or, for an Entry named in a Head, to the
peer that sent that Head, or, for a Manifest or Grant, to the peer that listed it. When several peers
qualify, a node SHOULD spread wants by each peer's outstanding count (§7.5). A `NotFound` removes that peer
as a source for that hash until a later `Have` from it.

### 10.5 Push

Whenever a node's listable state for a Package changes (§7.3), it sends `PackageChanged` to every
established peer that may see the Package except the one the change came from, followed by `Have` for the
new objects. A receiving peer proceeds as in §10.2. This bounds the latency of a new message by the
network's latency across any number of hops; no node polls.

### 10.6 Order of verification and the held buffer

A node applies objects in this order, whatever order they arrive: Roster (needs its user to be known) →
Manifest (self-verifying; needs the Package to be followed) → Grant (needs the Manifest) → Entry (needs
the writer's Roster and, for a grantee, the Grant) → payload objects (need nothing but their hash). An
object that arrives before what it needs is held as unverifiable (§8.1) and re-examined when its
prerequisite arrives.

The held buffer is bounded: at most 8 MiB per session and 64 MiB in total, oldest dropped beyond that, and
an object that remains unverifiable 10 minutes after its sender disconnected is dropped. A node SHOULD not
`Want` what it cannot verify (§10.2 step 3), so the buffer is for reordering, not for trust.

## 11. Local data handling

### 11.1 Storage

A node stores every object it verified under its hash and nothing else. Writes MUST be atomic with respect
to a crash: an object is either absent or complete, never partial; the RECOMMENDED way is to write to a
temporary name and rename into place, flushing the file and, on platforms that need it, the directory.
Object bytes are never modified; an object is only added or removed.

Metadata (users, devices, Packages, grants, heads, pins, peers) is kept in the node's database. Exactly one
process opens the store and the database; other local programs reach the node through its local protocol
(DESIGN.md §10.4). Programs the node trusts MAY read stored objects directly, read-only; they are immutable
and verified.

### 11.2 Pins and retention

A **pin** is a reason to keep an object: `Head` (it is a Head or an ancestor needed to verify one),
`Policy` (the retention policy of a Package wants it), `User` (the user said keep), `Service` (a local
service put the object or asked to keep it; see the local protocol). Pins are held per object and reason. An object with no pin is a candidate for eviction.

Retention policies, per Package and device class, with these defaults:

| Device class | Archive | Channel |
|---|---|---|
| desktop | every Version, every Tree, every ChunkList, every Chunk | every Message, every attachment |
| phone | every Version, the newest Version's Tree and ChunkLists; Chunks on demand | every Message; attachments on demand |

"On demand" objects are fetched when the user opens them and are evictable afterwards. The Manifest, the
Rosters of involved users, the Grants and every Entry of a followed Package are always pinned on every
device class: the history is small and is what makes everything else verifiable.

### 11.3 Eviction

When the store exceeds its budget, a node evicts unpinned objects least-recently-used first until under
budget. A node MUST NOT evict an object it has promised with `Have` to a peer while a `Want` for it from that
peer is outstanding; it finishes the `Object` first. After eviction a node MUST NOT claim the object.

### 11.4 Writing files to disk

A service that materialises a Tree on disk MUST write a file only when it holds every chunk, MUST verify the
ChunkList's `size`, and MUST apply the name rules of §8.4. Executable mode is applied where the file system
supports it. Symlinks are created only if the device policy allows it; by default a symlink is written as a
regular file holding the target text.

## 12. Errors and violations

### 12.1 Protocol violations

A violation is any of: a frame outside its bounds or not completed in time, an unknown message id 0 to 16,
a message in the wrong state, a message field outside its stated limits, a malformed `string` in a
message, a `Hello` that fails its checks, an `Object` whose hash does not match, a `Want` that exceeds the
outstanding bound, a second `ServiceOpen` while one's own is outstanding, or a `Packages` listing that
breaks the rules of §7.2. On a violation a node MUST close the connection with `Bye` (reason `Violation`,
or `Unauthorized` for a failed `Hello`) and SHOULD record it against the device. A device that causes three
violations within an hour SHOULD NOT be dialled for an hour.

### 12.2 Faulty content

Content that is well-formed on the wire but wrong in substance (a malformed object, an invalid signature
whose key is known, an Entry whose parents fail §8.6, a Manifest with another kind, a ChunkList whose size
is wrong) is not a violation: the connection continues, the content is dropped or marked, and the sender is
recorded as faulty for that object. A node MUST NOT forward faulty content.

### 12.3 Resource limits

Every limit in this document is a bound an implementation MUST enforce: frame size and completion time,
list and string sizes, object size, outstanding wants, queued object bytes, held bytes, recorded sources,
listing size and time, handshake and hello timeouts, idle timeout. Exceeding a wire limit is a violation;
exceeding a local limit (queued bytes, held bytes, recorded sources) applies back-pressure or dropping,
never a close.

## 13. Security considerations

- **Authentication** rests on TLS 1.3 with both certificates pinned to roster keys. There is no CA; the
  roster is the trust root, and it is signed by the user key, which is the only long-lived secret. No
  session tickets and no early data, so every connection is judged against current rosters.
- **Integrity** rests on content addressing and signatures: nothing a peer says about an object is believed,
  only the object's bytes and its signature. A peer can withhold or delay, never alter or forge. Low-s and
  deterministic signatures (§2.2) ensure one signed content has one name, so a reader cannot mint
  duplicates of a message or a concurrent twin of a Version.
- **Ownership** is bound into the Package id (§2.3, §8.7): no one but the owner's key can produce a Manifest
  for an id, so a reader who learns an id cannot squat it. User keys travel in Manifests, Grants and
  Rosters, so every object of a followed Package is verifiable by whoever may read it, not only by users
  who paired with every writer.
- **Freshness.** Manifests, Rosters and Grants carry `seq`; Entries carry clocks and are append-only. A peer
  can serve an older state, not roll one back: a node keeps the newest it has seen. A revocation reaches the
  revoked user (§9.7). There is no defence against a peer that hides a newer head it holds, beyond asking
  other peers.
- **Clocks.** A writer can choose a clock up to 2^32 above its parents' to sort last; more is faulty (§8.6).
  This is visible to peers and bounded by the fact that only roster devices can write.
- **Confidentiality** on the wire is TLS. At rest, objects are plaintext in this version; a device is
  trusted with what it may read (§9.7). Encryption at rest is reserved for a later version through object
  tag 8; nothing here precludes it.
- **Visibility** (§9.7, §9.8) is enforced by every serving node independently. A granted user can copy what
  they read; grants bound who the mesh serves, not what readers do. Accepted leak: a grantee learns the ids
  and device lists of the owner and of other grantees of the Packages they may see (§9.8); the full grantee
  list is visible to the owner's devices only.
- **Pairing** between users is out of this protocol: it is the exchange of user public keys by a channel the
  users trust (a QR code read in person, a short authentication string compared aloud). The pairing exchange
  MUST deliver each user's current Roster object together with the key, so that AMP/1 can begin: without a
  roster, no device of the other user would pass §4.3.
- **Key compromise.** A device key: remove the device from the roster; every node that receives the roster
  stops talking to it, and the removal is permanent for that key. A user key: there is no recovery in AMP/1;
  the user is new, and their Packages are re-created. Users SHOULD keep the user key on one device and back
  it up encrypted.
- **Denial of service** is bounded by admission and the limits of §12.3: unverifiable objects per session, recorded
  sources per peer, listing size and time, outstanding wants, frame completion time. A node on a hostile LAN
  costs an attacker a TLS handshake per attempt and nothing else; the handshake caps of the host apply.
- **Metadata.** Discovery reveals that a node exists and its ids; a passive observer learns who talks to whom
  and how much. Not defended.

## 14. Registries

### 14.1 Message ids

| Id | Message | § |
|---|---|---|
| 1 | Hello | 7.1 |
| 2 | Packages | 7.2 |
| 3 | PackageChanged | 7.3 |
| 4 | Have | 7.4 |
| 5 | Want | 7.5 |
| 6 | Object | 7.6 |
| 7 | NotFound | 7.7 |
| 8 | Cancel | 7.8 |
| 9 | RosterQuery | 7.9 |
| 10 | ServiceOpen | 7.10 |
| 11 | ServiceAccepted | 7.10 |
| 12 | ServiceRefused | 7.10 |
| 13 | Ping | 7.11 |
| 14 | Pong | 7.11 |
| 15 | Bye | 7.13 |
| 16 | RosterNotFound | 7.9 |

Ids 17 to 255 are reserved for AMP/1 extensions and are ignored when unknown (§1.4); 256 and above are never
assigned.

### 14.2 Bye reasons

| Value | Reason |
|---|---|
| 0 | Unspecified |
| 1 | Shutdown |
| 2 | Duplicate (§4.5) |
| 3 | Violation (§12.1) |
| 4 | Unauthorized (§7.1, §8.8) |
| 5 | Idle (§6.3) |

### 14.3 Capability flags

| Bit | Meaning |
|---|---|
| 0 | the sender follows new granted Packages automatically |
| 1 | the sender is a full mirror (desktop retention) for every Package it lists |
| 2 to 31 | reserved; MUST be sent as 0, ignored when received |

### 14.4 ALPN and service protocol names

| Name | Use |
|---|---|
| `axiom-mesh/1` | this protocol |
| `axiom-mesh-pair/1` | the pairing exchange; a separate document |
| `axiom-mesh-chat/1`, `axiom-mesh-sync/1` | service protocols, each its own document; names ending in `/<n>` carry their own version |

### 14.5 Signature and hash domains

| Use | Domain |
|---|---|
| Package id | `axiom-mesh/package/1` |
| Entry | `axiom-mesh/entry/1` |
| Manifest | `axiom-mesh/manifest/1` |
| Roster | `axiom-mesh/roster/1` |
| Grant | `axiom-mesh/grant/1` |

### 14.6 Object tags

| Tag | Object | § |
|---|---|---|
| 1 | Chunk | 8.2 |
| 2 | ChunkList | 8.3 |
| 3 | Tree | 8.4 |
| 4 | Entry | 8.6 |
| 5 | Manifest | 8.7 |
| 6 | Roster | 8.8 |
| 7 | Grant | 8.9 |

Tag 8 is reserved for a sealed (encrypted) object; 9 to 255 for extensions. Unknown tags are opaque (§1.4).

### 14.7 Entry kinds

| Kind | Entry | Package kind |
|---|---|---|
| 1 | Version | Archive |
| 2 | Message | Channel |
| 3 | Redaction | Channel |

### 14.8 Package kinds

| Kind | Name |
|---|---|
| 1 | Archive |
| 2 | Channel |

### 14.9 Ports

| Port | Use |
|---|---|
| 41810/tcp | mesh listener, default |
| 41811/udp | discovery broadcast |

The local protocol between the node and programs on the same device runs over a pipe, not a port
(DESIGN.md §10.4), and is not part of AMP/1.

## 15. Conformance

An implementation conforms to AMP/1 if it satisfies every MUST in this document. A test suite for
conformance covers, at least: every message encoded and decoded at every byte split; every limit refused one
over and accepted at the limit, with the message-versus-object distinction of §5.2; every signed object
verified, and found invalid after one bit of its body is flipped (which also renames it) or found malformed
after `s` is replaced by `n − s`; the Package id derivation of §8.7 accepted and refused; the total order,
the leaf rule and the merge table of §9 against fixed inputs including a criss-cross merge; the visibility
rules of §9.7 and §9.8 against a Package with and without a Grant and against a revocation; the relay rule of
§7.3 across three nodes; the duplicate rule of §4.5 in all three cases; and the establishment rule of §6.2
against a peer that closes after the handshake. Test vectors (encoded objects with their hashes and
signatures under published test keys) are published with the reference implementation and are part of this
specification once they exist.

## Appendix A. Message and object layouts at a glance

```
frame        := length:u32 messageId:u16 payload
Hello        := version:u16 deviceId:id userId:id rosterSeq:u64 capabilities:u32 services:list<string>
Packages     := page:u16 last:bool packages:list<PackageState>
PackageState := packageId:id manifestHash:hash manifestSeq:u64 grants:list<hash> heads:list<Head>
Head         := deviceId:id entryHash:hash clock:u64
PackageChanged := state:PackageState
Have | Cancel := hashes:list<hash>(1024);   Want := hashes:list<hash>(64)
Object       := hash:hash object:blob
NotFound     := hash:hash
RosterQuery | RosterNotFound := userId:id
ServiceOpen | ServiceAccepted := service:string
ServiceRefused := service:string reason:u8
Ping | Pong  := nonce:u64
Bye          := reason:u8 detail:string

object       := tag:u8 version:u8 body
Chunk        := data
ChunkList    := size:u64 chunks:biglist<hash>
Tree         := entries:list<TreeEntry>;  TreeEntry := name:string mode:u8 hash:hash size:u64
Entry        := packageId:id deviceId:id clock:u64 parents:list<hash> kind:u16 payload:blob signature
Manifest     := packageId:id ownerId:id ownerKey:pubkey nonce:bytes<32> kind:u16 seq:u64 name:string
                created:u64 retention:u8 branches:list<Branch> signature
Roster       := userId:id userKey:pubkey seq:u64 devices:list<RosterDevice> signature
Grant        := packageId:id granteeId:id granteeKey:pubkey rights:u8 seq:u64 signature
```

## Appendix B. Worked example

Alice's laptop (device L) and phone (device P) share a Channel `C` owned by Alice; Bob's desktop (device D)
holds a `Read` grant. Alice and Bob paired in person, which gave each the other's user key and current Roster.

1. **Discovery.** D sees L's DNS-SD record with `d=<L>`; Alice is a known user to D and her Roster lists L.
   D reserves admission for L's address, dials, and both sides complete TLS 1.3 with pinned keys.
2. **Hello.** Both send `Hello`. D checks that L's `deviceId` is `H(K_L)` and that L is in Alice's Roster.
   L does the same for D with Bob's Roster. Both sessions are established.
3. **Packages.** L lists `C` to D because Bob holds a `Read` grant: manifest hash, the hash of Bob's own
   Grant and no other, heads `{L: e7, clock 7}` and `{P: e6, clock 6}`. D holds the Manifest (with Alice's
   key and the nonce that derives `C`) and the Grant, and `e5`; it `Want`s `e7` and `e6`, since L and P are
   Alice's devices.
4. **Walk.** L sends `Object(e7)`, `Object(e6)`. D verifies both signatures against Alice's Roster, checks
   their clocks against their parents, stores them, sees `e7`'s parent `e5` is held and `e6`'s parent `e4` is
   held, and `Want`s the attachment Tree named in `e7` because D is a desktop. L answers with the Tree, then D
   wants its ChunkLists and Chunks.
5. **Push.** Alice types on P. P writes `e8` (clock 8, parent `e6`) and sends `PackageChanged` to L. L stores
   `e8`, which changes what L would list, so L sends `PackageChanged` to D (not back to P). D wants `e8` from
   L, verifies, and shows the message in the order `(clock, deviceId)`: after `e7` because 8 > 7.
6. **Refusal.** Bob's old phone, removed from Bob's Roster, dials L. L's TLS validation finds the key marked
   removed and closes without a byte; the old phone sees its session open and close before any `Hello`, and
   backs off.
7. **Revocation.** Alice revokes Bob: a Grant with `rights = 0`, `seq` 2. L sends `PackageChanged` to D with
   the new Grant's hash, which D may see because it names Bob. D wants it, verifies it against Alice's key
   from the Manifest, and stops listing `C` to anyone. D keeps `e4` to `e8`: they were valid when stored.
