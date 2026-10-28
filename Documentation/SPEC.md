# Axiom Mesh Protocol, Version 1 (AMP/1)

**Status:** Draft, 2026-10-28. Normative for the first implementation. Where this document and
[DESIGN.md](DESIGN.md) differ, this document wins; DESIGN.md explains why things are as they are, this document
says exactly what they are.

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
Appendix A. Message layouts at a glance
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
| **Object** | A byte string stored and transferred as a unit, named by its hash (§8). |
| **Package** | A signed history of objects with one owner (§9). |
| **Entry** | One signed step in a Package's history (§8.6). |
| **Head** | The newest entry one device wrote to a Package (§9.3). |
| **Roster** | A user's signed list of devices (§8.8). |
| **Grant** | An owner's signed statement that a user may read or write a Package (§8.9). |
| **Known user** | A user whose public key this node holds, through pairing or through a roster it accepted. |
| **Established session** | A session on which both sides have received the other's `Hello` (§6.2). |

### 1.3 Notation

Byte layouts are written as `name: type` lists in wire order. Types are defined in §5.2. All multi-byte
integers are big-endian. Sizes in this document use binary units: 1 KiB = 1024 bytes, 1 MiB = 1024 KiB.

### 1.4 Versioning

This is protocol version 1. The version is carried in the TLS ALPN (`axiom-mesh/1`), in `Hello` (§7.1) and in
the first byte of every object after its tag (§8.1). A node MUST refuse a peer whose major version differs,
by not offering the ALPN. Changes that keep every valid AMP/1 byte sequence valid and meaning the same thing
MAY be made within version 1 by adding message ids, object tags, entry kinds or capability flags in the
registries of §14; anything else is version 2.

## 2. Cryptographic primitives and identifiers

### 2.1 Hash

`H(x)` is SHA-256 of `x`, 32 bytes. Every object name, user id, device id and Package id is an output of `H`.

### 2.2 Signatures

Signatures are ECDSA over the NIST P-256 curve with SHA-256, encoded as the 64-byte IEEE P1363 fixed form
`r || s`, each 32 bytes big-endian. A signature over a structure is computed over
`H(domain || body)`, where `domain` is the structure's domain string from §14.5 encoded as UTF-8 without a
terminator, and `body` is every byte of the structure that precedes the signature field, exactly as encoded on
the wire. A verifier MUST hash the bytes it received, never a re-encoding of its own.

### 2.3 Keys and identifiers

A key pair is an ECDSA P-256 key pair. A public key is encoded on the wire as the 65-byte uncompressed point
`0x04 || X || Y` (`pubkey` in §5.2).

| Identifier | Definition |
|---|---|
| User id | `H(user public key)` over the 65-byte encoding |
| Device id | `H(device public key)` over the 65-byte encoding |
| Package id | `H("axiom-mesh/package" ‖ owner user id ‖ nonce)`, `nonce` 32 random bytes chosen at creation |

Ids are 32 bytes and never change. A user key MUST NOT be used as a device key or the other way round; a
node that sees a device id equal to a known user id MUST treat the roster entry as invalid.

### 2.4 Certificates

Each device holds a self-signed X.509 certificate whose subject public key is the device key. The
certificate MUST carry the Digital Signature key usage and both the server and client authentication extended
key usages. Validity SHOULD be ten years; a node MUST renew its certificate before expiry and MAY do so at any
time, since peers pin the key and not the certificate (§4.3). No other field of the certificate carries
meaning. The subject name SHOULD be `CN=<device id in lowercase hex>` for logs.

### 2.5 Randomness

Nonces and keys MUST come from a cryptographically secure random source.

## 3. Discovery

Discovery answers which addresses on the local network claim to run a node and which device id each claims.
A claim is a hint, never trusted: only the TLS handshake (§4.3) proves it. A node MUST NOT dial a discovered
address whose claimed device id is not in a roster it holds (its own or a known user's).

### 3.1 mDNS / DNS-SD

A node SHOULD announce the service `_axiom-mesh._tcp` with the instance name equal to the device id in
lowercase hex, the port of its mesh listener, and these TXT records:

| Key | Value |
|---|---|
| `v` | `1` |
| `d` | device id, lowercase hex, 64 characters |
| `u` | user id, lowercase hex, 64 characters |

Unknown keys MUST be ignored. A record whose `v` is not `1` MUST be ignored.

### 3.2 UDP broadcast

Where multicast is filtered, a node MAY broadcast on UDP port 41811 and MAY listen on it. The datagram is:

```
magic:      bytes<4>   = "AMD1"
flags:      u8         bit 0: 1 = announcement, 0 = query
deviceId:   id
userId:     id
port:       u16        the TCP port of the mesh listener
```

A query carries the sender's own ids and port and asks every listener to answer with an announcement, sent
unicast to the query's source address. A node MUST NOT send more than one announcement per second per
interface and MUST ignore datagrams of any other length or magic. Nothing in the datagram is authenticated;
it is a hint like a DNS-SD record.

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
`TLS_AES_256_GCM_SHA384` and `TLS_CHACHA20_POLY1305_SHA256`. Renegotiation is not used. The ALPN MUST be
negotiated; a connection without a negotiated ALPN MUST be closed. ALPNs are listed in §14.4.

The TLS handshake MUST complete within 10 seconds of the TCP connection, or the connection is closed.

### 4.3 Peer validation

Each side validates the other's certificate by the public key alone:

1. Let `K` be the peer certificate's subject public key and `D = H(K)` (§2.3).
2. `D` MUST appear in a roster this node holds, as a device that is not removed. The roster is either this
   node's own user's roster or the roster of a known user.
3. Nothing else is checked: no certificate chain, no name, no revocation, no validity period.

A failed check MUST close the connection without sending a protocol byte. The accepting side can do this
before its handshake completes; the dialling side, by the nature of TLS 1.3, may only learn of its own
rejection as a closed connection after its handshake completed (see §6.2).

### 4.4 Dialling

A node dials a peer when discovery or a static address names a device in a roster it holds and no
established session to that device exists. A node SHOULD NOT dial the same address more often than once per
10 seconds after a failure, with exponential backoff to at most 5 minutes. Admission limits (§4.6) MUST be
decided before the TCP connection is opened.

### 4.5 Duplicate connections

Two nodes may dial each other at once. When a node finds that it holds, or is about to hold, two
established sessions to the same device id, it keeps the session that was *dialled by the device with the
lower device id* (compared as 32-byte unsigned big-endian integers) and closes the other with
`Bye(reason = Duplicate)` (§7.12). Both sides reach the same decision without talking.

### 4.6 Admission

A node MUST bound the connections it serves: in total, per peer address, and per second. The defaults are
those of the host (LWSE: 10 000 in total, 600 per client, 50 per second per client); a mesh node SHOULD set
per-client to a small number such as 4, since a device needs one session. Admission applies to accepted and
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
them; it MAY stream a frame's payload. An unknown `messageId` is a violation.

### 5.2 Types

| Type | Encoding |
|---|---|
| `u8`, `u16`, `u32`, `u64` | unsigned, big-endian, fixed width |
| `bool` | `u8`, 0 or 1; any other value is a violation |
| `bytes<n>` | exactly n bytes |
| `hash`, `id` | `bytes<32>` |
| `pubkey` | `bytes<65>`, `0x04 ‖ X ‖ Y` |
| `signature` | `bytes<64>`, `r ‖ s` |
| `string(max)` | `u16` byte length, then that many bytes of UTF-8, at most `max` bytes, no NUL, valid UTF-8 |
| `blob(max)` | `u32` byte length, then that many bytes, at most `max` |
| `list<T>(max)` | `u16` count, then that many `T`, at most `max` |
| `biglist<T>(max)` | `u32` count, then that many `T`, at most `max` |

A `string` that is not valid UTF-8 or contains NUL is a violation. A length over its stated maximum is a
violation, and a receiver MUST detect it from the length field before reading the content.

### 5.3 Canonical form

Every structure in this document has exactly one valid encoding: fixed field order, no optional fields, no
padding, lists in the order the structure states (sorted where it says so). Two encoders given the same
values produce the same bytes. This is what makes hashing and signing well defined (§2.2, §8.1).

## 6. Session

### 6.1 States

```
          send Hello, receive Hello
Connected ─────────────────────────► Established ──ServiceOpen/Accepted──► Switched
    │                                     │                                    │
    └──── Bye or close ◄──────────────────┴──────────── service close ◄────────┘
```

- **Connected:** the TLS handshake completed with ALPN `axiom-mesh/1`. Each side MUST send `Hello` as its
  first frame, immediately, without waiting for the peer's. No other frame MAY be sent or accepted before
  `Hello`. If no `Hello` arrives within 10 seconds, the connection is closed.
- **Established:** both `Hello`s were received and validated (§7.1). Every message of §7 may flow in either
  direction at any time. The protocol is symmetric: there is no client and no server above TLS.
- **Switched:** the connection was handed to a service protocol (§7.10). AMP/1 frames are not exchanged until
  the service protocol ends and the session returns to Established.

### 6.2 Establishment and refusal

A session counts as established for a node only once it has received the peer's `Hello`. A node MUST NOT
treat the completion of its own TLS handshake as acceptance by the peer (§4.3). A connection that closes
before the peer's `Hello` arrived MUST be recorded as a refusal of this node by that peer, and the node
SHOULD back off from that peer as for a failed dial (§4.4).

### 6.3 Liveness

A node MUST send `Ping` when it has sent nothing for 60 seconds and MUST answer `Ping` with `Pong` carrying
the same nonce. A session on which nothing was received for 5 minutes MUST be closed. A `Pong` with an
unknown nonce is ignored.

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
(§4.3); `deviceId` is a device of `userId` in the roster the node holds for that user. A failing check is a
violation and the connection is closed with `Bye(reason = Unauthorized)`.

If the peer's `rosterSeq` is lower than the sequence number this node holds for that user, the node SHOULD
send the roster to the peer: `Have` of the roster object followed by nothing else, so that the peer may
`Want` it (§10.3), or directly `Object` since it is small. If it is higher, the node SHOULD `Want` the roster
by announcing nothing and waiting for the peer to send it, or ask with `RosterQuery` (§7.9).

### 7.2 `Packages` (2), Established only

```
page:       u16                 0-based page number
last:       bool                true on the final page
packages:   list<PackageState>(256)
```

```
PackageState:
  packageId:      id
  manifestHash:   hash            the newest manifest object the sender holds
  manifestSeq:    u64
  grants:         list<hash>(256) the grant objects the sender holds for this Package, newest per grantee
  heads:          list<Head>(256)
Head:
  deviceId:       id
  entryHash:      hash
  clock:          u64
```

A node MUST send its complete `Packages` listing once after establishment, and again whenever the set of
Packages it may list to this peer changes. The listing MUST contain exactly the Packages for which the
peer's user holds a `Read` or `Write` grant (§8.9) or is the owner, and MUST NOT contain any other. Pages
MUST be sent in order; a receiver replaces its view of the sender's state when `last` is true.

### 7.3 `PackageChanged` (3), Established only

```
state:      PackageState
```

Sent to every established peer that may see the Package, the moment a local write changed a head, the
manifest or a grant. Also sent when a grant now gives the peer access to a Package it could not see before.
A receiver MUST treat it as an update to its view from `Packages`.

### 7.4 `Have` (4), Established only

```
hashes:     list<hash>(1024)
```

"I hold these objects." A node MUST send `Have` for the subset it holds of every `Want` it received, and MAY
send it unsolicited when it stored new objects of a Package the peer may see. A node MUST NOT claim an object
it does not hold or that the peer may not see (§10.5).

### 7.5 `Want` (5), Established only

```
hashes:     list<hash>(1024)
```

"Send these if you have them." The receiver answers each hash with `Object` or `NotFound`, in any order, and
MUST answer every hash eventually unless a `Cancel` for it arrives first. A node MUST NOT have more than 64
hashes outstanding (wanted, not yet answered or cancelled) toward one peer.

### 7.6 `Object` (6), Established only

```
hash:       hash
object:     blob(4194304)       the object's bytes, §8
```

`hash` MUST equal `H(object)`; a mismatch is a violation. A receiver MUST verify the hash and the object's
well-formedness (§8) before storing or forwarding it, and MUST NOT act on an object before that. An `Object`
the receiver did not want MAY be dropped; a node SHOULD NOT send unsolicited objects except a roster after
`Hello` (§7.1).

A sender MUST NOT have more than 8 MiB of `Object` payload queued toward one peer; past that it stops
reading `Want`s from that peer until the queue drains.

### 7.7 `NotFound` (7), Established only

```
hash:       hash
```

The answer to a `Want` the sender cannot satisfy, including an object the receiver may not see (§10.5): the
two cases are indistinguishable on the wire, deliberately.

### 7.8 `Cancel` (8), Established only

```
hashes:     list<hash>(1024)
```

The sender no longer wants these. The receiver MAY drop queued `Object`s for them and need not answer them.

### 7.9 `RosterQuery` (9), Established only

```
userId:     id
```

Asks the peer for the newest roster object it holds for that user; the peer answers with `Object` or
`NotFound`.

### 7.10 `ServiceOpen` (10) / `ServiceAccepted` (11) / `ServiceRefused` (12), Established only

```
ServiceOpen:
  service:    string(64)          a service protocol name, §14.4
ServiceAccepted:
  service:    string(64)
ServiceRefused:
  service:    string(64)
  reason:     u8                  1 unknown, 2 busy, 3 not allowed
```

`ServiceOpen` asks to switch the connection to the named service protocol. The receiver answers
`ServiceAccepted`, after which both sides speak the service protocol until it ends by its own rules and the
session returns to Established; or `ServiceRefused`. A node MUST NOT send any AMP/1 frame between
`ServiceOpen` and the answer except `Ping`/`Pong`. Both sides MUST NOT have a `ServiceOpen` outstanding at
once: if both sent one, the one from the lower device id wins and the other is answered `ServiceRefused(busy)`.

### 7.11 `Ping` (13) / `Pong` (14)

```
nonce:      u64
```

Allowed in every state after `Hello` was sent. `Pong` echoes the nonce.

### 7.12 `Bye` (15)

```
reason:     u8                  §14.2
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

Its name is `H(object)` over all of its bytes. An object MUST be at most 4 MiB. An object whose tag is
unknown, whose version is not 1, or whose body does not parse exactly to its end (no trailing bytes) is
malformed; a malformed object MUST NOT be stored and its sender SHOULD be recorded as faulty (§12.2).

Signed objects (Entry, Manifest, Roster, Grant) end in a `signature` field; §2.2 defines what it covers. A
signed object whose signature does not verify is **unverifiable**, not malformed: it MAY be stored while the
key that would verify it is unknown (the roster may arrive later), but MUST NOT be acted on until verified.

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

`size` MUST equal the sum of the chunks' sizes; a receiver verifies that once it holds the chunks. The
maximum file size in AMP/1 is therefore 32 GiB.

### 8.4 `Tree` (tag 3)

```
entries:    list<TreeEntry>(8192)
TreeEntry:
  name:     string(255)
  mode:     u8                  1 file, 2 executable file, 3 directory, 4 symlink
  hash:     hash                mode 1, 2: a ChunkList; 3: a Tree; 4: a Chunk holding the link target
  size:     u64                 modes 1, 2: the file size; 3: 0; 4: the target length
```

Entries MUST be sorted by `name` as byte strings and MUST be unique. A `name` MUST NOT be empty, `.` or
`..`, and MUST NOT contain `/`, `\` or NUL. An implementation MUST refuse to write a tree to disk whose
names, on that file system, would escape the tree's directory or collide case-insensitively, and SHOULD
record the conflict as it records a merge conflict (§9.5).

### 8.5 Entry payloads

An Entry's `payload` (§8.6) is a blob whose layout depends on `kind` (§14.7):

```
Version (kind 1):
  tree:         hash
  label:        string(64)       the branch name, "main" by default
  prerelease:   string(64)       empty, or a pre-release tag such as "beta.2"
  notes:        string(4096)
Message (kind 2):
  text:         string(16384)
  attachments:  list<Attachment>(64)
  replyTo:      hash             all zero when none
  Attachment:
    name:       string(255)
    hash:       hash             a Tree or ChunkList
    size:       u64
Redaction (kind 3):
  target:       hash             the entry withdrawn
```

A payload that does not parse exactly to its end is malformed. An unknown `kind` is not malformed: the entry
is stored, replicated and ordered, and its payload is opaque (a node without the service for that kind still
carries the Package).

### 8.6 `Entry` (tag 4)

```
packageId:  id
deviceId:   id                  the writing device
clock:      u64                 Lamport clock, §9.3
parents:    list<hash>(2)       entries this one follows; 0 for the first entry of a Package
kind:       u16                 §14.7
payload:    blob(1048576)       §8.5
signature:  signature           by the device key, domain "axiom-mesh/entry/1"
```

An Entry is **valid** when its signature verifies against the public key of `deviceId` in the roster of a user
who is the Package's owner or holds a `Write` grant (§8.9) at the time of verification, and every parent, once
held, belongs to the same `packageId`. A node MUST NOT act on an invalid Entry and MUST NOT forward it.

### 8.7 `Manifest` (tag 5)

```
packageId:  id
ownerId:    id
kind:       u16                 the Package kind, §14.8
seq:        u64
name:       string(255)
created:    u64                 milliseconds since the Unix epoch; informational
retention:  u8                  0 default, 1 everything, 2 latest only; a hint to the device policy
branches:   list<Branch>(64)
  Branch:
    name:   string(64)
    head:   hash                the entry the owner declares as the branch head; all zero when unset
signature:  signature           by the owner's user key, domain "axiom-mesh/manifest/1"
```

A node MUST hold at most one current Manifest per Package: the valid one with the highest `seq`, ties
broken by the lower hash. A Manifest whose `packageId` or `ownerId` differs from the one the node already
holds for that Package MUST be ignored. `kind` MUST NOT change between manifests of one Package.

### 8.8 `Roster` (tag 6)

```
userId:     id
seq:        u64
devices:    list<RosterDevice>(256)
  RosterDevice:
    deviceId:   id
    key:        pubkey
    name:       string(64)
    addedAt:    u64             informational
    removed:    bool
signature:  signature           by the user key, domain "axiom-mesh/roster/1"
```

`deviceId` MUST equal `H(key)` for every device, else the roster is malformed. A node holds at most one
current Roster per user, the valid one with the highest `seq`. A device marked `removed` MUST stay in every
later roster of that user, so that peers learn the removal; a roster that drops a device without marking it
removed is accepted but SHOULD be logged. A user's roster is valid only if its signature verifies against
the user key the node holds for `userId`; a Roster for an unknown user MUST NOT be stored.

Removal takes effect when the roster reaches a node: sessions with the removed device MUST be closed with
`Bye(Unauthorized)`, no new session is accepted from it, and Entries it signs are invalid from then on.
Entries already stored stay.

### 8.9 `Grant` (tag 7)

```
packageId:  id
granteeId:  id                  a user id
rights:     u8                  0 none (revocation), 1 read, 2 write (implies read)
seq:        u64
signature:  signature           by the owner's user key, domain "axiom-mesh/grant/1"
```

A node holds at most one current Grant per (`packageId`, `granteeId`): the valid one with the highest `seq`.
The owner needs no grant. A grant's validity requires the owner's user key, taken from the Package's
Manifest, which the node MUST hold first.

## 9. Packages

### 9.1 Identity and ownership

A Package is identified by its `packageId` (§2.3) and owned by the user in its Manifest. The owner is the only
user who may sign Manifests and Grants for it. Ownership does not change in AMP/1.

### 9.2 Kinds

A Package's kind (§14.8) says what its Entries mean. Kind 1 `Archive` holds `Version` entries; kind 2
`Channel` holds `Message` and `Redaction` entries. A node MUST replicate, store and order Entries of any
kind; it interprets payloads only for kinds it has a service for.

### 9.3 Clocks and heads

Every Entry carries a Lamport clock: the writing device sets `clock` to one more than the highest `clock` of
any Entry it holds for the Package (its parents included), or 0 for the first Entry. A Head is the newest
Entry a device wrote; a Package has one Head per writing device. A node learns heads from `Packages` and
`PackageChanged` and MUST keep, per Package and per writing device, the Entry with the highest `clock` it
holds, ties broken by the lower hash.

The **total order** of Entries in a Package is by `(clock, deviceId, hash)` ascending. Every node that holds
the same set of Entries computes the same order.

### 9.4 Channels

A Channel's visible order is the total order. There is no merge: a device appends with its own head as the
single parent. A `Redaction` is honoured when its `deviceId` equals the target's, or its signer is the
Package owner; otherwise it is ignored. A node MUST keep the redacted Entry's bytes (it is part of the
history others verify) and MUST NOT show its payload.

### 9.5 Archives, branches and merges

A `Version` names a branch by `label`. The head of a branch on a device is the newest `Version` with that
label in the total order among the Entries the device holds. A node that holds two `Version`s with the same
label that are not ancestors of one another MUST, when it next writes to that branch, write a `Version` with
both as `parents` (a merge) whose tree is derived as follows, path by path over the union of the two trees
against their nearest common ancestor's tree (the empty tree if none):

| Changed on | Result |
|---|---|
| neither side | the ancestor's entry |
| one side only | that side's entry |
| both sides, identically | that entry |
| both sides, differently | the entry from the side whose `Version` is later in the total order, plus the other side's file written beside it as `<name>.conflict-<first 8 hex of the other deviceId>` (before any extension) |

A directory replaced by a file, or the reverse, counts as "changed differently". No content merge is
performed. The owner's Manifest `branches` list MAY name a head per branch; a node MUST treat it as the
owner's declaration for display and MUST NOT let it override the order above.

### 9.6 Who may write

An Entry is valid only if signed by a device of the owner or of a `Write` grantee (§8.6). A node MUST
re-check validity when it receives a new Roster or Grant, and MUST stop forwarding Entries that became
invalid; it MAY keep them.

### 9.7 Visibility

A node MAY list, claim (`Have`), or send (`Object`) an object of a Package to a peer only if the peer's user
is the owner or holds a current `Read` or `Write` grant for that Package. An object held under several
Packages is visible if any of them is. Chunks, ChunkLists and Trees are visible through the Packages whose
Entries reference them; a node answering `Want` for a hash MUST resolve visibility through the objects it
knows reference that hash, and MUST answer `NotFound` when none that the peer may see does.

## 10. Replication

### 10.1 Goal

After enough time connected, two peers that may see the same Package hold the same Entries for it, and
each holds the objects its retention policy (§11.2) asks for. Replication is driven by state, not by
events: every `Packages` listing lets a node recompute what it wants.

### 10.2 On establishment

Each side sends `Packages` (§7.2). On receiving a `PackageState` for a Package it follows, a node compares:

1. **Manifest:** if `manifestSeq` is higher than its own, it `Want`s `manifestHash`.
2. **Grants:** it `Want`s every grant hash it does not hold.
3. **Heads:** for every Head whose `entryHash` it does not hold, it `Want`s the hash.

For a Package it does not follow but is granted, a node that follows new Packages automatically (a device
policy) MAY start following it by the same steps.

### 10.3 Walking history

On storing a valid Entry, a node `Want`s every parent it does not hold, until it reaches Entries it holds.
Then, for each new Entry, it `Want`s the objects the retention policy asks for: the Entry's payload objects
(a `Version`'s Tree, then the Tree's children, recursively; a `Message`'s attachments) as far as the policy
goes. A node MUST NOT `Want` an object it holds and SHOULD NOT `Want` the same object from two peers at
once except when a peer has not answered within 30 seconds.

### 10.4 Choosing a source

Wants are addressed to a peer that announced `Have` for the hash, or, for an Entry named in a Head, to the
peer that sent that Head. When several peers qualify, a node SHOULD spread wants by each peer's outstanding
count (§7.5). A `NotFound` removes that peer as a source for that hash until a later `Have` from it.

### 10.5 Push

On a local write, a node sends `PackageChanged` to every established peer that may see the Package (§9.7),
followed by `Have` for the new objects. A receiving peer proceeds as in §10.2. This bounds the latency of a
new message by the network's latency; no node polls.

### 10.6 Ordering of verification

A node applies objects in this order, whatever order they arrive: Roster (needs the user key, which it
holds) → Manifest (needs the owner's roster or user key) → Grant (needs the Manifest) → Entry (needs the
writer's roster and, for a grantee, the Grant) → payload objects (need nothing but their hash). An object
that arrives before what it needs is held as unverifiable (§8.1) and re-examined when its prerequisite
arrives. A node MUST bound what it holds unverified, 64 MiB by default, dropping the oldest beyond that.

## 11. Local data handling

### 11.1 Storage

A node stores every object it accepted under its hash. An object MUST be verified (§8.1) before it is
stored. Writes MUST be atomic with respect to a crash: an object is either absent or complete, never
partial; the RECOMMENDED way is to write to a temporary name and rename into place, flushing the file and,
on platforms that need it, the directory. Object bytes are never modified; an object is only added or
removed.

Metadata (users, devices, Packages, grants, heads, pins, peers) is kept in the node's database. Exactly one
process opens the database; other local programs reach the node through its management API.

### 11.2 Pins and retention

A **pin** is a reason to keep an object: `Head` (it is a Head or an ancestor needed to verify one),
`Policy` (the retention policy of a Package wants it), `User` (the user said keep). Pins are held per object
and reason. An object with no pin is a candidate for eviction.

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

A service that materialises a Tree on disk (the Sync service) MUST write a file only when it holds every
chunk, MUST verify the ChunkList's `size`, and MUST apply the name rules of §8.4. Executable mode is applied
where the file system supports it. Symlinks are created only if the device policy allows it; by default a
symlink is written as a regular file holding the target text.

## 12. Errors and violations

### 12.1 Protocol violations

A violation is any of: a frame outside its bounds, an unknown message id, a message in the wrong state, a
field outside its stated limits, a malformed `string`, a `Hello` that fails its checks, an `Object` whose
hash does not match, more than 64 outstanding wants from a peer, or a `ServiceOpen` while one is pending.
On a violation a node MUST close the connection with `Bye` (reason `Violation`, or `Unauthorized` for a
failed `Hello`) and SHOULD record it against the device. A device that causes three violations within an
hour SHOULD NOT be dialled for an hour.

### 12.2 Faulty content

Content that is well-formed on the wire but wrong in substance (a malformed object, an invalid signature
whose key is known, an Entry whose parents are in another Package, a Manifest with a changed owner) is not a
violation: the connection continues, the content is dropped, and the sender is recorded as faulty for that
object. A node MUST NOT forward faulty content.

### 12.3 Resource limits

Every limit in this document is a bound an implementation MUST enforce: frame size, list sizes, string
sizes, object size, outstanding wants, queued object bytes, unverified bytes, handshake and hello timeouts,
idle timeout. Exceeding a wire limit is a violation; exceeding a local limit (queued bytes) applies
back-pressure, never a close.

## 13. Security considerations

- **Authentication** rests on TLS 1.3 with both certificates pinned to roster keys. There is no CA; the
  roster is the trust root, and it is signed by the user key, which is the only long-lived secret.
- **Integrity** rests on content addressing and signatures: nothing a peer says about an object is believed,
  only the object's bytes and its signature. A peer can withhold or delay, never alter or forge.
- **Freshness.** Manifests, Rosters and Grants carry `seq`; Entries carry clocks and are append-only. A peer
  can serve an older state, not roll one back: a node keeps the newest it has seen. There is no defence
  against a peer that hides a newer head it holds, beyond asking other peers.
- **Confidentiality** on the wire is TLS. At rest, objects are plaintext in this version; a device is
  trusted with what it may read (§9.7). Encryption at rest is reserved for a later version through a new
  object tag; nothing here precludes it.
- **Visibility** (§9.7) is enforced by every serving node independently. A granted user can copy what they
  read; grants bound who the mesh serves, not what readers do.
- **Pairing** between users is out of this protocol: it is the exchange of user public keys by a channel the
  users trust (a QR code read in person, a short authentication string compared aloud). AMP/1 begins once
  both hold the other's key.
- **Key compromise.** A device key: remove the device from the roster; every node that receives the roster
  stops talking to it. A user key: there is no recovery in AMP/1; the user is new, and their Packages are
  re-created. Users SHOULD keep the user key on one device and back it up encrypted.
- **Denial of service** is bounded by admission and the limits of §12.3. A node on a hostile LAN costs an
  attacker a TLS handshake per attempt and nothing else; the handshake caps of the host apply.
- **Metadata.** Discovery reveals that a node exists and its ids; a passive observer learns who talks to whom
  and how much. Not defended.
- **Lamport clocks** are not time. A device may write a future clock to sort last; this is visible to its
  peers and bounded by the fact that only roster devices can write.

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
| 15 | Bye | 7.12 |

Ids 16 to 255 are reserved for AMP/1 extensions; 256 and above are never assigned.

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
| 2 to 31 | reserved, MUST be 0 |

### 14.4 ALPN and service protocol names

| Name | Use |
|---|---|
| `axiom-mesh/1` | this protocol |
| `axiom-mesh-pair/1` | the pairing exchange; a separate document |
| `axiom-mesh-chat/1`, `axiom-mesh-sync/1` | service protocols, each its own document; names ending in `/<n>` carry their own version |

### 14.5 Signature domains

| Structure | Domain |
|---|---|
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

Tag 8 is reserved for a sealed (encrypted) object in a later version.

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
| 41812/tcp | management API, loopback only; not part of AMP/1 |

## 15. Conformance

An implementation conforms to AMP/1 if it satisfies every MUST in this document. A test suite for
conformance covers, at least: every message encoded and decoded at every byte split; every limit refused one
over and accepted at the limit; every signed object verified and rejected with one flipped bit; the total
order and the merge table of §9 against fixed inputs; the visibility rule of §9.7 against a Package with
and without a grant; and the establishment rule of §6.2 against a peer that closes after the handshake. Test
vectors (encoded objects with their hashes and signatures under published test keys) are published with the
reference implementation and are part of this specification once they exist.

## Appendix A. Message layouts at a glance

```
frame        := length:u32 messageId:u16 payload
Hello        := version:u16 deviceId:id userId:id rosterSeq:u64 capabilities:u32 services:list<string>
Packages     := page:u16 last:bool packages:list<PackageState>
PackageState := packageId:id manifestHash:hash manifestSeq:u64 grants:list<hash> heads:list<Head>
Head         := deviceId:id entryHash:hash clock:u64
PackageChanged := state:PackageState
Have | Want | Cancel := hashes:list<hash>
Object       := hash:hash object:blob
NotFound     := hash:hash
RosterQuery  := userId:id
ServiceOpen | ServiceAccepted := service:string
ServiceRefused := service:string reason:u8
Ping | Pong  := nonce:u64
Bye          := reason:u8 detail:string

object       := tag:u8 version:u8 body
Chunk        := data
ChunkList    := size:u64 chunks:biglist<hash>
Tree         := entries:list<TreeEntry>;  TreeEntry := name:string mode:u8 hash:hash size:u64
Entry        := packageId:id deviceId:id clock:u64 parents:list<hash> kind:u16 payload:blob signature
Manifest     := packageId:id ownerId:id kind:u16 seq:u64 name:string created:u64 retention:u8 branches:list<Branch> signature
Roster       := userId:id seq:u64 devices:list<RosterDevice> signature
Grant        := packageId:id granteeId:id rights:u8 seq:u64 signature
```

## Appendix B. Worked example

Alice's laptop (device L) and phone (device P) share a Channel `C` owned by Alice; Bob's desktop (device D)
holds a `Read` grant.

1. **Discovery.** D sees L's DNS-SD record with `d=<L>`; D holds Alice's roster (from pairing), which lists L.
   D reserves admission for L's address, dials, and both sides complete TLS 1.3 with pinned keys.
2. **Hello.** Both send `Hello`. D checks that L's `deviceId` is `H(K_L)` and that L is in Alice's roster.
   L does the same for D with Bob's roster. Both sessions are established.
3. **Packages.** L lists `C` to D because Bob holds a `Read` grant: manifest hash, the grant's hash, heads
   `{L: e7, clock 7}` and `{P: e6, clock 6}`. D holds the manifest and grant and `e5`; it `Want`s `e7` and
   `e6`.
4. **Walk.** L sends `Object(e7)`, `Object(e6)`. D verifies both signatures against Alice's roster, stores
   them, sees `e7`'s parent `e5` is held and `e6`'s parent `e4` is held, and `Want`s the attachment Tree
   named in `e7` because D is a desktop. L answers with the Tree, then D wants its ChunkLists and Chunks.
5. **Push.** Alice types on P. P writes `e8` (clock 8, parent `e6`), sends `PackageChanged` to L, which
   sends it on to D. D wants `e8` from L, verifies, and shows the message in the order `(clock, deviceId)`:
   after `e7` because 8 > 7.
6. **Refusal.** Bob's old phone, removed from Bob's roster, dials L. L's TLS validation finds the key marked
   removed and closes without a byte; the old phone sees its session open and close before any `Hello`,
   and backs off.
