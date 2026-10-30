# Axiom Mesh Pairing Protocol, Version 1 (axiom-mesh-pair/1)

**Status:** Draft 2, 2026-10-30. Normative for the first implementation. Draft 2 records what the first
implementation decided where draft 1 was silent: pairing with one's own user (§2.3, §7.1), the order in which the
two sides store, close and dial (§5.1, §6.2, §9). It specifies the exchange that
[SPEC.md](SPEC.md) §13 and §14.4 leave to "a separate document" and that [LOCAL-SPEC.md](LOCAL-SPEC.md) §5.15 and §6.2
drive through the local protocol. Where this document restates a rule of SPEC.md, SPEC.md wins; where it restates a rule
of LOCAL-SPEC.md about what a client sees, LOCAL-SPEC.md wins. [DESIGN.md](DESIGN.md) §5.2 explains why pairing works
this way.

**Abstract.** `axiom-mesh-pair/1` is the short exchange that makes two users known to each other, or adds a device to a
user's Roster. One side shows a **code**: a line of text naming its addresses and its device key. The other side enters
it, dials one of the addresses and pins that key in a TLS 1.3 handshake under its own ALPN. Both sides send their user
key and current Roster, both screens show the same **short authentication string** derived from every key involved,
and the pairing completes only when both users confirm that the strings match. A pairing runs one at a time per node
and expires after 10 minutes; nothing is stored unless both users confirmed.

## Table of contents

1. Introduction
2. The pairing code
3. Modes
4. Connection
5. Messages
6. State machine
7. Checks
8. Short authentication string
9. Results
10. Security considerations
11. Registries
12. Conformance
Appendix A. Layouts at a glance
Appendix B. Worked example

## 1. Introduction

### 1.1 Requirements language

The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED, MAY and OPTIONAL are to be
interpreted as described in RFC 2119 and RFC 8174 when, and only when, they appear in all capitals.

### 1.2 Terminology

| Term | Meaning |
|---|---|
| **Showing side** | The side that shows a code and listens: mode `ShowUser` or `ShowDevice` (§3). |
| **Entering side** | The side that is given the code, dials and pins the shown key: mode `JoinUser` or `AddDevice`. |
| **User side** | A side that holds an identity: every mode but `ShowDevice`. |
| **Device side** | The side being enrolled into a user's Roster: mode `ShowDevice`. It holds a device key and no user. |
| **Code** | The text the showing side shows and the entering side is given (§2). |
| **SAS** | The short authentication string both users compare (§8). |
| **Pairing connection** | The TLS connection under ALPN `axiom-mesh-pair/1` that carries one pairing. |

The other terms (user, device, Roster, known user, `H`, `pubkey`, the wire types) are those of SPEC.md §1.2, §2 and
§5.2. Sizes use binary units; integers are big-endian.

### 1.3 Relation to AMP/1 and the local protocol

A pairing is driven by the local protocol: `StartPairing` starts it in one of the four modes of LOCAL-SPEC.md §14.10,
`ConfirmPairing` and `CancelPairing` are the users' decisions, and `Pairing` (LOCAL-SPEC.md §5.15) shows its state,
code and SAS to the client. This document specifies what happens between the two nodes. A pairing makes AMP/1
possible: after it, each side holds the other user's key and Roster (or, for an enrolled device, its own user's), so
the peer validation of SPEC.md §4.3 admits the other side's devices. Nothing of AMP/1 runs on a pairing connection.

## 2. The pairing code

### 2.1 Format

```
code      := "amp1" ":" mode ":" addresses ":" key
mode      := "user" | "device"
addresses := address *( "," address )
address   := host ":" port | "[" ipv6 "]" ":" port
key       := 87 characters: base64url (RFC 4648 §5) without padding of the 65-byte device key
```

`mode` is `user` for a code shown in mode `ShowUser` and `device` for one shown in mode `ShowDevice`. `key` is the
showing side's device key (SPEC.md §2.3), the key of the certificate its TLS stack presents (SPEC.md §2.4). The whole
code is at most 512 characters (the bound of `Pairing.code`, LOCAL-SPEC.md §5.15). Example, with the published test
key `alice-laptop` (`Documentation/vectors/`):

```
amp1:user:192.168.1.20:41810,[fe80::1%eth0]:41810:BAHQZP-5s6xgJVE9hQ4FtJpCkTC93gmPiMjGoweUonNDT0k_LgO8ucsM195yaEnZwKbeXkgxP5FHI-6Nr0Csa8k
```

A code is a plain string. An implementation MAY render it as a QR code; it MUST accept it typed or pasted.

### 2.2 Addresses

A code lists 1 to 8 addresses, in the order the entering side SHOULD try them, each with its TCP port: the showing
side's mesh listener (SPEC.md §4.1, default 41810), on every interface it serves the pairing on.

- `host` is a DNS name or an IPv4 address: ASCII letters, digits, `.` and `-`, 1 to 253 characters, not starting with
  `.` or `-`.
- `ipv6` is an IPv6 literal of hex digits, `:` and `.`, optionally followed by `%` and a zone of letters, digits, `_`,
  `.` and `-`. It is always written in brackets.
- `port` is 1 to 65 535 in decimal, without sign or leading zero.

### 2.3 Parsing

An entering side MUST accept a code only in exactly the form §2.1 and §2.2 write: the prefix `amp1`, a known mode, at
least one and at most 8 well-formed addresses, a key of exactly 87 base64url characters that decodes to a 65-byte
`pubkey` on P-256 (SPEC.md §2.3), and a text that the same values would write identically. Anything else is refused
before any connection is made. A code of mode `user` is entered in mode `JoinUser` only, one of mode `device` in mode
`AddDevice` only. A `user` code whose key is a device of the entering side's own user is refused as well: a device of
the own user is enrolled (`AddDevice`), never paired (§7.1).

## 3. Modes

| Mode | Shows or enters | Pairs with | Needs | Sends a user key and Roster |
|---|---|---|---|---|
| `ShowUser` | shows a `user` code | `JoinUser` | an identity | yes |
| `JoinUser` | enters a `user` code | `ShowUser` | an identity | yes |
| `ShowDevice` | shows a `device` code | `AddDevice` | no identity; a device key | no |
| `AddDevice` | enters a `device` code | `ShowDevice` | the user key | yes |

`ShowUser` and `JoinUser` pair two users: each ends knowing the other (§9.1). `ShowDevice` and `AddDevice` enrol a
device: the `AddDevice` side signs a new Roster of its user that lists the device, and the device adopts it (§9.2,
§9.3). A device in `ShowDevice` creates its device key before it shows the code, if it has none, and keeps it whatever
the outcome. The values of the modes are those of LOCAL-SPEC.md §14.10.

## 4. Connection

### 4.1 Dialling and listening

The showing side listens on the addresses of its code, which are its mesh listener, and the entering side dials them
in order until one completes the TLS handshake of §4.2. The connection is one TCP connection, as SPEC.md §4.1 says.
The entering side SHOULD stop trying when every address failed and report the pairing failed (§6.2).

While a pairing is in state `Waiting` on the showing side, its listener offers the ALPN `axiom-mesh-pair/1` beside
`axiom-mesh/1`; at every other time it MUST NOT offer it. A node MUST NOT run more than one pairing at a time (§6.3), so
it serves at most one pairing connection: a second one that reaches it while a pairing connection is open MUST be
answered with `PairCancel(Busy)` and closed.

### 4.2 TLS

A pairing connection is TLS 1.3 with the rules of SPEC.md §4.2 (forward-secret AEAD suites, no early data, no session
tickets, a handshake within 10 seconds), and these differences in what is validated:

- The **entering** side offers only the ALPN `axiom-mesh-pair/1` and presents its device certificate (SPEC.md §2.4).
  It MUST accept the server certificate only if its subject public key equals the key of the code (§2.1), and nothing
  else about the certificate is checked. A different key closes the connection without a byte: it is not the device
  whose code the user was given.
- The **showing** side MUST require a client certificate, and under ALPN `axiom-mesh-pair/1` accepts any certificate
  whose subject public key is a `pubkey` on P-256: it does not know the entering side yet, which is what the SAS (§8)
  is for. It MUST NOT accept an unknown key under ALPN `axiom-mesh/1`, where SPEC.md §4.3 applies unchanged.
- A negotiated ALPN other than `axiom-mesh-pair/1` is not a pairing connection; one without an ALPN is closed (SPEC.md
  §4.2).

Each side then knows the other's TLS key, called **its TLS key** below. A device's TLS key is its device key, so `H` of
it is its device id.

## 5. Messages

### 5.1 Framing and session

Frames are those of SPEC.md §5.1: a `u32` length of 2 to 4 198 400 counting the id and the body, a `u16` message id
(§11.1), the body. The types are those of SPEC.md §5.2, and a value outside its bounds, a body not read exactly to its
end, or an id other than 1 to 3 is a violation. There is no extension range.

The session is symmetric. Each side sends `PairHello` as its first frame, immediately, without waiting for the other's;
before the other's `PairHello` it accepts only `PairHello` and `PairCancel`. After it, each side accepts one
`PairConfirm` and any `PairCancel`, and sends its own `PairConfirm` (§6.2) or a `PairCancel`. A second `PairHello` or
`PairConfirm` is a violation. A `PairHello` must arrive within 10 seconds, and a pairing connection on which nothing
arrives for 10 minutes ends with `PairCancel(Expired)`, which is also how it outlives the pairing's expiry (§6.3). On a
violation a side sends `PairCancel(Violation)` and closes. A side that sent or received `PairCancel` sends nothing more
and closes. Once the pairing reached `Paired`, the showing side closes after its last frame, its results stored (§9);
the entering side reads on until the showing side closed, then closes and dials (§9), so the showing side has stored
its results when the entering side's first AMP/1 connection arrives. There is no keep-alive: the
users' comparison takes the time it takes, bounded by the expiry.

### 5.2 `PairHello` (1)

```
version:    u16             = 1
mode:       u8              the sender's mode, §3 and §11.3
deviceId:   id              H(the sender's TLS key)
name:       string(64)      a display name: the device's in ShowDevice, the user's otherwise
userKey:    bytes<65>       the sender's user key (a pubkey); 65 zero bytes in ShowDevice
roster:     blob(4194304)   the sender's user's current Roster object (SPEC.md §8.8); empty in ShowDevice
```

`userKey` is the one field that may be absent, encoded as 65 zero bytes; any other value that is not a `pubkey` on P-256
is a violation. `name` is shown to the other user (LOCAL-SPEC.md §5.15 `remoteName`); for a device being enrolled it is
the name the device proposes for its Roster entry. The receiver checks the message by §7.1.

### 5.3 `PairConfirm` (2)

```
roster:     blob(4194304)   the new Roster that enrols the device, from the AddDevice side; empty from every other side
```

"My user confirmed that both screens show the same SAS." From the `AddDevice` side it carries the Roster of §9.2,
which the device checks by §7.2; every other side sends it empty, and a receiver in a mode other than `ShowDevice`
ignores its content.

### 5.4 `PairCancel` (3)

```
reason:     u8              §11.2; other values are read as 0, unspecified
detail:     string(256)     for logs and the user; MUST NOT be interpreted
```

The pairing ends without success (§6.2). The receiver sends nothing more and closes. Unlike AMP/1's `Bye`, a malformed
`PairCancel` is a violation like any other malformed message; the outcome is the same, a failed pairing.

## 6. State machine

### 6.1 States

The states are those of LOCAL-SPEC.md §6.2 and §14.10, in order:

| State | Meaning |
|---|---|
| `Waiting` | The showing side shows its code and waits for a pairing connection; the entering side is dialling. |
| `Confirm` | The other side's `PairHello` passed §7.1; the SAS is set and shown; the user compares it. |
| `Confirming` | This side's user confirmed; the other side has not yet, or, on the `AddDevice` side, the device has not yet. |
| `Paired` | Both users confirmed; the results of §9 are stored. |
| `Failed` | Expired, refused by a check, broken by a violation, or disconnected. |
| `Cancelled` | A user cancelled, on this side or the other. |

`Paired`, `Failed` and `Cancelled` are final: a pairing that reached one never changes again.

### 6.2 Transitions

| In | Event | Then | Sends |
|---|---|---|---|
| `Waiting` | the other side's `PairHello` passes §7.1 | `Confirm` | — |
| `Waiting` | it fails §7.1 | `Failed` | `PairCancel(Refused)` |
| `Waiting` | the pairing connection closes before a `PairHello` | showing side: `Waiting`; entering side: `Failed` | — |
| `Waiting` | the user confirms | unchanged; the request is `Invalid` | — |
| `Confirm` | the user confirms, mode `ShowUser`, `JoinUser` or `ShowDevice` | `Confirming`, or `Paired` if the other side's `PairConfirm` arrived | `PairConfirm` (empty) |
| `Confirm` | the user confirms, mode `AddDevice` | `Confirming`, or `Paired` if the device's `PairConfirm` arrived | the enrolling `PairConfirm` (§9.2) once `Paired` |
| `Confirm` | the other side's `PairConfirm` arrives, mode `ShowUser`, `JoinUser` or `AddDevice` | `Confirm`, remembering it | — |
| `Confirm` | a `PairConfirm` arrives, mode `ShowDevice` | `Failed`: the user side may not enrol a device before the device's user confirmed | `PairCancel(Violation)` |
| `Confirming` | the user confirms again | unchanged; the request is `OK` | — |
| `Confirming` | the other side's `PairConfirm` arrives (on `ShowDevice`: and passes §7.2) | `Paired` | mode `AddDevice`: the enrolling `PairConfirm` (§9.2) |
| `Confirming` | on `ShowDevice`, the enrolling `PairConfirm` fails §7.2 | `Failed` | `PairCancel(Refused)` |
| any but final | the user cancels | `Cancelled` | `PairCancel(Cancelled)` |
| any but final | `PairCancel(Cancelled)` arrives | `Cancelled` | — |
| any but final | any other `PairCancel` arrives | `Failed` | — |
| `Confirm`, `Confirming` | the pairing connection closes | `Failed` | — |
| any but final | a violation (§5.1) | `Failed` | `PairCancel(Violation)` |
| any but final | the pairing expires (§6.3) | `Failed` | `PairCancel(Expired)` |
| final | the user confirms or cancels | unchanged; the request is `NotFound` | — |

The users confirm in either order, except in the device modes: the `AddDevice` side sends its `PairConfirm`, which
carries the enrolling Roster, only after the device's `PairConfirm` arrived and its own user confirmed. A device
therefore never receives a Roster that lists it unless its own user agreed first. A side that reaches `Paired` stores its results before it
sends its last frame, and the connection closes as §5.1 says; the `ShowDevice` side and, in the user modes, the side whose
`PairConfirm` came second, reach it on receipt.

### 6.3 Expiry and one pairing at a time

A pairing expires 10 minutes after it started (`StartPairing`, LOCAL-SPEC.md §6.2), measured on each side by its own
clock: a pairing not final at its expiry fails. A node runs at most one pairing at a time: a pairing may start only when
the last one is final or expired, and a second `StartPairing` is answered `Busy`. While its pairing waits, a showing side
serves at most one pairing connection (§4.1).

## 7. Checks

### 7.1 `PairHello`

On receipt of the other side's `PairHello` a side MUST check, in this order:

1. `version` is 1;
2. `mode` is the counterpart of its own (§3): `ShowUser` with `JoinUser`, `ShowDevice` with `AddDevice`;
3. `deviceId` equals `H` of the other side's TLS key (§4.2);
4. if `mode` is `ShowDevice`: `userKey` is 65 zero bytes and `roster` is empty;
5. otherwise: `roster` parses as a Roster (SPEC.md §8.8), its signature verifies under its own `userKey`, that key equals
   `userKey`, and the Roster lists `deviceId` as a device that is not removed;
6. in modes `ShowUser` and `JoinUser`: `userKey` is not this side's own user key; a device of the own user is enrolled,
   never paired.

A failed check fails the pairing with `PairCancel(Refused)`, whose detail names the check. The `AddDevice` side then
also checks that the device can be enrolled: a device whose id any Roster of its user ever removed or omitted cannot be
listed again (SPEC.md §8.8), and is refused the same way, before the users are asked to compare anything.

Check 5 binds the TLS key that the handshake proved to the user key through a signature of that user: a side that
pairs learns not a key someone claims, but a Roster that only the user key could have signed and that names the very
device it is talking to.

### 7.2 Enrolment

The `ShowDevice` side MUST accept the `roster` of the `AddDevice` side's `PairConfirm` only if it parses as a Roster,
its signature verifies under its own user key, that key equals the `userKey` of the `AddDevice` side's `PairHello`, its
`seq` is higher than that of the Roster the `PairHello` carried, and it lists this device's own key as a device that is
not removed. Otherwise the pairing fails with `PairCancel(Refused)`.

## 8. Short authentication string

Both sides derive the same string from the four keys involved:

```
sas = base32(H("axiom-mesh/pair/1" ‖ sort(userKeyA, userKeyB) ‖ sort(tlsKeyA, tlsKeyB)))[0..8]
```

- `userKeyA`, `userKeyB` are both sides' user keys from their `PairHello`s as 65-byte `pubkey`s; the device side's is
  65 zero bytes. `tlsKeyA`, `tlsKeyB` are both sides' TLS keys as the handshake proved them (§4.2), never as a message
  claims them.
- `sort` puts the two keys of a pair in ascending order as unsigned byte strings, so both sides compute the same
  input whichever of them is A.
- The domain is the 17 bytes of `axiom-mesh/pair/1` in UTF-8, without a terminator.
- `base32` is the alphabet of RFC 4648 §6 (`A` to `Z`, `2` to `7`), most significant bit first; the first 8 characters
  are the first 40 bits of the hash.

A side shows the string as two groups of four, `XXXX-XXXX`, for example `K7QD-M2XA`, and SHOULD show it in a font that
tells `2` from `Z` and `5` from `S`. The users compare the strings out of band (side by side, or read aloud) and confirm
only if they are equal.

## 9. Results

Each side stores its results before it sends the frame that lets the other side reach `Paired`, and the entering side,
once the showing side closed the pairing connection (§5.1), dials the showing device under `axiom-mesh/1` at the
address of the code that answered.

Nothing is stored before `Paired`; a `Failed` or `Cancelled` pairing leaves both sides as they were, except that a
`ShowDevice` side keeps the device key it created.

### 9.1 `ShowUser` and `JoinUser`

On `Paired`, each side stores the other side's user key and its Roster from the `PairHello`: the other user becomes a
known user through pairing (SPEC.md §1.2), and the Roster becomes that user's current one unless a newer one is held
(SPEC.md §8.8). The display name is stored for the user's interface. From then on the other user's devices pass the
peer validation of SPEC.md §4.3, and either side MAY dial the other under `axiom-mesh/1`.

### 9.2 `AddDevice`

On reaching `Paired`, the `AddDevice` side signs with its user key a new Roster: the current one with `seq` one higher
and the device added, its key the device's TLS key, its name the `PairHello`'s `name`, `addedAt` the current time and
not removed. It stores that Roster as its user's current one, sends it in its `PairConfirm`, and sends it to every
established peer as SPEC.md §7.6 requires of a Roster whose `seq` rose. The user key never leaves the node.

### 9.3 `ShowDevice`

On receiving an enrolling `PairConfirm` that passes §7.2 (and so reaching `Paired`), the device stores the user's id
and public key and the new Roster as its user's current one, and with it its own place in it: it now has an identity
without the user key (LOCAL-SPEC.md §6.2, `Identity.userKeyPresent` false). It then dials its user's other devices
under `axiom-mesh/1` as any node does (SPEC.md §4.4).

## 10. Security considerations

- **The code is the trust anchor.** The entering side pins the device key of the code (§4.2), so a man in the middle
  cannot answer for the showing side without its private key. The code is not secret, but it must reach the entering
  user unaltered: shown on the other user's screen, read off a QR code in person, or copied over a channel both trust.
  An altered code would pin the attacker's key; the SAS then still differs (below), but users who skip the comparison
  would pair with the attacker.
- **The SAS is the check for the showing side.** The showing side cannot know who connects; anyone on the network who
  sees the code may connect while the pairing waits. The SAS covers both user keys and both TLS keys, so a party that
  connects in place of the intended one produces a string that no screen of the intended user shows, and its user does
  not confirm. Both sides must confirm (§6.2), so neither user alone completes a pairing.
- **Why the TLS keys are in the SAS.** They are the keys the handshake proved; the user keys in `PairHello` are only
  bound to them by the Roster's signature (§7.1). Covering both makes the string depend on what each side actually
  talks to, not only on what was claimed.
- **Searching for a matching string.** The SAS has 40 bits. An attacker who wants two connections to show the same
  string must find keys that collide with keys it learns only during the pairing; it cannot precompute, and the
  pairing expires after 10 minutes (§6.3), which bounds the search far below 2^40 key generations.
- **Enrolment.** The device adopts only a Roster signed by the user key the `AddDevice` side presented, newer than the
  one it showed, and listing the device's own key (§7.2); it receives such a Roster only after its own user confirmed
  (§6.2). The user key never travels. A device once removed is never enrolled again (§7.1, SPEC.md §8.8).
- **Exposure while waiting.** The pairing ALPN is offered only while a pairing waits (§4.1), one pairing connection is
  served at a time, a `PairHello` must arrive within 10 seconds, frames are bounded as in AMP/1 (§5.1), and nothing a
  stranger sends is stored. A stranger can block a waiting pairing (the intended user is answered `Busy`); the users
  notice that their screens do not progress and start again. A key accepted under the pairing ALPN is never accepted
  under `axiom-mesh/1` because of it: only the stored results of §9 change what SPEC.md §4.3 admits.
- **Not defended.** Users who confirm without comparing; a code shown to the wrong person; a compromised device that
  holds the user key.

## 11. Registries

### 11.1 Message ids

| Id | Message | § |
|---|---|---|
| 1 | PairHello | 5.2 |
| 2 | PairConfirm | 5.3 |
| 3 | PairCancel | 5.4 |

Every other id is never assigned in version 1; a frame carrying one is a violation.

### 11.2 Cancel reasons

| Value | Reason | Meaning |
|---|---|---|
| 0 | Unspecified | no reason given, or one the receiver does not know |
| 1 | Cancelled | a user cancelled |
| 2 | Busy | the receiver runs another pairing, or already serves a pairing connection |
| 3 | Refused | a check of §7 failed |
| 4 | Expired | the pairing expired, or no `PairHello` arrived in time |
| 5 | Violation | a violation of §5.1, or an enrolment before the device confirmed |

### 11.3 Modes, states, names

The modes and states and their values are those of LOCAL-SPEC.md §14.10. The ALPN `axiom-mesh-pair/1` is registered in
SPEC.md §14.4; the SAS domain `axiom-mesh/pair/1` (§8) is used for nothing else.

## 12. Conformance

An implementation conforms to `axiom-mesh-pair/1` if it satisfies every MUST in this document. A test suite for
conformance covers, at least: every message encoded and decoded at every byte split, and every bound refused one over
and accepted at the bound; a code written and parsed back, and every malformed form of §2.3 refused; each check of §7.1
and §7.2 refused on its own; the SAS equal on both sides and different when any one of the four keys is substituted,
including the enrolment case with one absent user key; every transition of §6.2, including `Invalid` while waiting,
`OK` unchanged while confirming, `Busy` for a second pairing and the failure at exactly 10 minutes; and a device never
listed again once removed.

## Appendix A. Layouts at a glance

```
frame       := length:u32 messageId:u16 payload                                  (as SPEC.md §5.1)
PairHello   := version:u16 mode:u8 deviceId:id name:string(64) userKey:bytes<65> roster:blob(4194304)
PairConfirm := roster:blob(4194304)
PairCancel  := reason:u8 detail:string(256)
code        := "amp1:" ("user" | "device") ":" address *("," address) ":" base64url(device key)
sas         := base32(H("axiom-mesh/pair/1" ‖ sort(userKeys) ‖ sort(tlsKeys)))[0..8], shown XXXX-XXXX
```

## Appendix B. Worked example

Alice (user key A, laptop L) pairs with Bob (user key B, desktop D); later she enrols her new phone P.

1. **Show.** On L, Alice starts a pairing in mode `ShowUser`. L's listener now offers `axiom-mesh-pair/1`, and L shows
   `amp1:user:192.168.1.20:41810:<L's key>`.
2. **Join.** Bob enters the code on D in mode `JoinUser`. D dials 192.168.1.20:41810 offering only
   `axiom-mesh-pair/1`, presents its certificate and accepts L's only because its key is the code's.
3. **Hello.** Both send `PairHello`: L with A and Alice's Roster (listing L), D with B and Bob's Roster (listing D). Each
   checks the other's by §7.1 and derives the SAS from A, B, L and D: both screens show, say, `K7QD-M2XA`.
4. **Confirm.** Bob compares the strings with Alice and confirms on D first: D sends `PairConfirm` and waits in
   `Confirming`. Alice confirms on L: L sends `PairConfirm` and, having D's, reaches `Paired`; D reaches it on receipt.
   Both close the pairing connection. Each now knows the other user and holds their Roster; D dials L under
   `axiom-mesh/1` and SPEC.md §4.3 admits it.
5. **Enrol.** On the new phone P, which has no identity, a pairing in mode `ShowDevice` creates P's device key and shows
   `amp1:device:...:<P's key>`. Alice enters it on L in mode `AddDevice`. P's `PairHello` carries no user; L's carries A
   and her Roster (seq 4). L checks that P was never removed from her Roster. Both show the SAS of A, a zero key, L and
   P.
6. **Enrolment.** Alice confirms on L first: L waits in `Confirming`, sending nothing. She confirms on P: P sends
   `PairConfirm`. L, now holding P's confirmation, signs Roster seq 5 listing L and P, stores it, sends it in its
   `PairConfirm` and pushes it to Bob's desktop over AMP/1. P checks it by §7.2, stores A, Alice's id and the Roster, and
   is Alice's device: it dials L and D under `axiom-mesh/1`.
