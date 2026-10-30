# Structural debt

Where the code (or the plan it is built by) does not follow [ARCHITECTURE.md](Documentation/ARCHITECTURE.md) or the
specifications yet. Each entry states the rule, what the code does instead, the evidence, why that is tolerated for
now, and the step that pays it. An entry is re-read against the code before it is cited, and removed in the commit
that fixes it.

## When debt is paid

1. **Cheap and isolated: at once.** A fix of an hour or less that touches no design still to come is never carried.
2. **Otherwise in the step that next rebuilds the code it touches.** That step writes the tests the fix needs anyway.
3. **Correctness and security before the next release.** Debt that can lose an Entry, a connection or a key does not
   ship. Debt about structure and performance may, and the release notes do not hide it.
4. **A rule waiting for a decision says so.** ARCHITECTURE.md changes only on the maintainer's instruction.

Each entry names the step that pays it: a milestone of the fundamentals plan (M1–M14) or a later phase of
DESIGN.md §17. Numbers are stable: a paid entry's number is not reused.

## 1. Signatures are not deterministic

- **Rule:** SPEC.md §2.2: signers SHOULD use deterministic nonces (RFC 6979), so one signer signing the same bytes
  twice produces the same object; §5.3 and §13 lean on it for "one content, one name".
- **Instead:** the BCL's ECDSA draws a random nonce; signing the same Entry twice yields two valid objects with two
  names.
- **Evidence:** `System.Security.Cryptography.ECDsa.SignHash` has no deterministic mode on .NET 8 or 10.
- **Tolerated because:** the SHOULD is not a MUST; every signature is still low-s and verifies, and an Entry is signed
  once per write. Golden vectors of signed objects are verified, never byte-compared.
- **Paid in:** not planned; when the BCL offers RFC 6979 or Ed25519 is decided (DESIGN.md §15.3).

## 2. mDNS discovery is not tested in CI

- **Rule:** a promise only with the test that proves it (§9).
- **Instead:** multicast is filtered on hosted CI runners; the mDNS shell (`MdnsDiscovery`) is exercised by hand only:
  two instances in one process on Windows found each other beside the operating system's responder (M4).
- **Evidence:** fundamentals plan, decision D7; M4 records mDNS as manual.
- **Tolerated because:** the DNS-SD codec is pure and fully tested; broadcast and static peers are tested on loopback.
- **Paid in:** not planned for CI; the manual check is part of `ACCEPTANCE.md` (M13/M14).

## 3. PIPE-ACCESS.md is not written

- **Rule:** LOCAL-SPEC.md §2.4 defers the exact ACL, socket modes, service account and allow list to `PIPE-ACCESS.md`.
- **Instead:** the document does not exist.
- **Evidence:** `Documentation/` holds no `PIPE-ACCESS.md`.
- **Tolerated because:** the listeners (M8) carry the effect §2.1 and §2.2 require: an owner-only access list with
  `FILE_FLAG_FIRST_PIPE_INSTANCE` and `PIPE_REJECT_REMOTE_CLIENTS` on Windows, a 0600 socket file on Linux, the process
  identity behind both, and `pipe.allowUsers` for a system service (`Axiom.Mesh.Host/README.md` documents the keys).
  What is missing is the normative text.
- **Paid in:** M13, with the documents of the release.

## 4. KEY-BACKUP.md is not written, and the user key has no backup

- **Rule:** LOCAL-SPEC.md §6.1 specifies the user key's backup apart, in `KEY-BACKUP.md`.
- **Instead:** neither the document nor the export/import requests exist; losing the device that holds the user key
  loses the user's identity.
- **Evidence:** fundamentals plan, decision D7.
- **Tolerated because:** phase 1a is desktop only and accepts re-creating an identity.
- **Paid in:** a later phase (DESIGN.md §17, "later").

## 5. Branch leaves, the merge table and conflict names are not built

- **Rule:** SPEC.md §9.5 and §15 (the leaf rule and the merge table, including a criss-cross merge).
- **Instead:** Model implements no merge; `UpdatePackage` takes no branches.
- **Evidence:** fundamentals plan §2.1 ("minus §9.5 merge") and D7.
- **Tolerated because:** only the Sync service needs merges, and it is phase 1b.
- **Paid in:** phase 1b (Sync service).

## 6. Telemetry series are not recorded

- **Rule:** LOCAL-SPEC.md §10.
- **Instead:** the telemetry requests answer `NotFound` and Host capability bit 2 is clear; counters go to LWSE's
  meter behind a sink only.
- **Evidence:** fundamentals plan, D7 and §3.7.
- **Tolerated because:** the requests answer as the specification allows, and nothing in phase 1a reads the series.
- **Paid in:** phase 3 (the app's mesh analytics).

## 7. Service calls and emitted events are not built

- **Rule:** LOCAL-SPEC.md §9.8 and §9.10 (`ServiceCall`, `ForwardedCall`, `Emit`).
- **Instead:** those requests answer `UnknownRequest`.
- **Evidence:** fundamentals plan, D7.
- **Tolerated because:** no service with a namespace exists in phase 1a; `UnknownRequest` keeps the Host conformant.
- **Paid in:** phase 2 (Chat service), the first service with a namespace.

## 8. Removing a device and forgetting a user have no user interface beyond the request

- **Rule:** DESIGN.md §5 (a lost device is removed from the roster; an unwanted user is forgotten).
- **Instead:** `RemoveDevice` and `ForgetUser` exist as requests; no CLI flow walks the user through them.
- **Evidence:** fundamentals plan, D7.
- **Tolerated because:** the requests and their effects are tested; the flow is presentation.
- **Paid in:** phase 3 (the app).

## 9. Objects are not encrypted at rest

- **Rule:** DESIGN.md §7.4 (designed for, not built).
- **Instead:** objects are stored in clear under the user's profile.
- **Evidence:** DESIGN.md §15.4.
- **Tolerated because:** the design's current answer for the first release is "no"; the store addresses bytes so that
  sealing can be added.
- **Paid in:** a later phase (DESIGN.md §17, "later").

## 10. Installing the Host as a Windows service or a systemd unit is documented, not automated

- **Rule:** DESIGN.md §10.5 (how the Host runs per platform).
- **Instead:** the user starts `axiom-meshd run` or registers it by hand following the documentation.
- **Evidence:** fundamentals plan, D7.
- **Tolerated because:** phase 1a is proven on two PCs by hand (ACCEPTANCE.md).
- **Paid in:** phase 3, with the desktop app's installer.

## 11. Pairing codes are text, not QR codes or six words

- **Rule:** DESIGN.md §5.2 and §17 phase 4 ("a phone pairs by QR code").
- **Instead:** a pairing code is a text string, printed as an ASCII QR code when stdout is a terminal.
- **Evidence:** fundamentals plan, D7.
- **Tolerated because:** desktops can copy text; a camera is a phone's.
- **Paid in:** phase 4 (Android, iOS).

## 12. The Host answers only the phase-1 request subset

- **Rule:** LOCAL-SPEC.md §15: every request of §6–§10 answered with its OK body and every listed error.
- **Instead:** M8 serves the session and infrastructure requests (`GetIdentity`, `CreateIdentity`, `RenameDevice`,
  `GetStore`, `ListServices`, `ListPeers`, `ListSessions`, `ListDiscovered`, `Subscribe`, `Unsubscribe`, `ListOperations`,
  `GetOperation`, `CancelOperation`); M9 adds the rest of the phase-1 set of the fundamentals plan §2.5; everything else
  answers `UnknownRequest` (`Axiom.Mesh.Core/Local/RequestRoutes.cs`).
- **Evidence:** fundamentals plan §2.5 and D7.
- **Tolerated because:** `UnknownRequest` is the specification's answer for a request a Host does not serve, so a
  half-built Host stays conformant; `CONFORMANCE.md` marks the missing rows planned.
- **Paid in:** the phases that need each request (entries 6, 7, 8 and 5 above).

## 13. The executables are named after their assemblies, not `axiom-meshd`

- **Rule:** DESIGN.md §11 and the fundamentals plan §1 name the daemon `axiom-meshd` and the CLI `axiom-mesh`.
- **Instead:** the build produces `Axiom.Mesh.Host` and `Axiom.Mesh.Cli`; only the CLI's tool command is `axiom-mesh`.
  The assembly names stay equal to the package names because the layering conventions read them.
- **Evidence:** `Axiom.Mesh.Host/Axiom.Mesh.Host.csproj` sets no `AssemblyName`.
- **Tolerated because:** nothing is published yet; the daemon prints and answers as `axiom-meshd`.
- **Paid in:** M12, with the Native AOT publish (rename the published binary, keep the assembly name).

## 14. The store directory is not flushed after a rename where .NET cannot open a directory

- **Rule:** SPEC.md §11.1: write to a temporary name and rename into place, "flushing the file and, on platforms that
  need it, the directory".
- **Instead:** `DirectoryObjectFiles` flushes the file, renames, and then tries to open the directory as a file stream
  to flush it; where the runtime refuses that (not verified on Linux yet: the suite ran on Windows, which needs no
  directory flush), the attempt is skipped silently.
- **Evidence:** `Axiom.Mesh.Core/Storage/DirectoryObjectFiles.cs`, `FlushDirectory`.
- **Tolerated because:** the object is still absent or complete after a crash (the rename is atomic); what can be lost
  is a just-renamed object, which the index (written after the file) does not name yet unless its batch committed.
- **Paid in:** M12, with the integration tests on Linux (an `fsync` on a directory descriptor through a P/Invoke-free API,
  or `File.OpenHandle` on the directory where the runtime allows it).

## 15. The index's composite keys are concatenated into one column

- **Rule:** DESIGN.md §13 keys `Devices`, `Grants`, `Heads` and `Pins` by several columns.
- **Instead:** LDB 0.1.0 as published on nuget.org allows one `[Key]` property; `[CompositeKey]` came after the
  package was built. These tables carry a `Key` column holding the parts one after the other (`RowKeys`), beside the
  parts as indexed columns of their own.
- **Evidence:** `Axiom.Mesh.Core/Storage/RowKeys.cs`; the generator's error LDB1003 on `[CompositeKey]` against 0.1.0.
- **Tolerated because:** byte-string order of the concatenation is the order of the parts, and lookups by the whole
  key or by one part are both indexed.
- **Paid in:** the first LDB release with composite keys: schema version 2 with a migration of those four tables.

## 16. Orphaned object files and temporary leftovers are not swept

- **Rule:** SPEC.md §11.1: a node stores every object it verified under its hash and nothing else.
- **Instead:** a batch whose transaction fails after its object files were written leaves those files without an index
  row, and a crash mid-write leaves a `<hash>.tmp-<random>` file; nothing removes either.
- **Evidence:** `Axiom.Mesh.Core/Store/StoreWriter.cs` (files first, then the transaction); `StoreTests.AnInterruptedWriteLeavesNoObject_ACompleteOneIsFoundUnderItsHash`.
- **Tolerated because:** both are verified bytes or never found under a hash; they cost disk only, and a later store
  of the same object reuses an orphan.
- **Paid in:** M9, with `GetStore` (a start-up sweep of temporary files and of files the index does not name).

## 17. Intake does not check an Entry's parents

Paid in e6a8f57 (M7): the store writer checks `Lamport.TryFindParentFault` against the stored parents before it stores
an Entry (`StoreBatch.HasParentFaultAsync`, `AdmitOutcome.Faulty`) and the session records the sender as faulty
(`FaultyContentTests`). What remains, an Entry stored before its parents, is entry 28.

## 18. Evicting an Entry forgets its Head without electing the next one

- **Rule:** SPEC.md §9.3: a node keeps, per Package and device, the stored Entry with the highest clock.
- **Instead:** when an Entry is evicted (possible only for a Package no longer followed, whose Entries are unpinned),
  its `Heads` row is deleted and no other stored Entry of the device takes its place until one is stored again.
- **Evidence:** `Axiom.Mesh.Core/Store/StoreBatch.cs`, `UnlinkAsync`.
- **Tolerated because:** an unfollowed Package is neither listed nor replicated; following it again fetches its Heads
  from the peers' listings, and M7's `HeadsView` mirrors the `Heads` table exactly.
- **Paid in:** M9, with the store requests (`Evict` and `GetStore`): electing the next stored Entry of the device on
  eviction, in the writer and in `HeadsView`.

## 19. A user who stops being known keeps the stored Roster

- **Rule:** SPEC.md §8.8: a node stores a valid Roster only for a known user, and MUST NOT hold or store one for any
  other.
- **Instead:** the store writer refuses a Roster of an unknown user, but when a user stops being known (the last
  followed Package naming them is unfollowed), their stored Roster stays in the index with its `Head` pin (source the
  user).
- **Evidence:** `Axiom.Mesh.Core/Store/StoreBatch.cs`, `AdmitRosterAsync` and `PackageAsync`.
- **Tolerated because:** the snapshot trusts devices of known users only, so the Roster is not acted on; following one
  of the user's Packages again needs it.
- **Paid in:** M9, with `ForgetUser` and the users requests.

## 20. A frame is not bounded to 30 seconds from its first byte

Paid in e6a8f57 (M7): `LinkActivity` follows every AMP/1 frame's length prefix as the bytes pass and arms one timer per
connection on the node's clock; a frame still incomplete 30 seconds after its first byte closes the session with
`Bye(Violation)` and counts against the device (`FaultyContentTests.AFrameNotCompletedWithin30SecondsClosesTheSession`).

## 21. The certificate callback cannot read the ALPN on every platform

- **Rule:** PAIR-SPEC.md §4.2: under `axiom-mesh/1` an unknown key MUST NOT be accepted; SPEC.md §4.3 lets the accepting
  side refuse before its handshake completes.
- **Instead:** while a pairing waits on the showing side, the certificate callback lets an unknown P-256 key through when
  the platform cannot yet say which ALPN was negotiated, and `PeerCheck.Decide` refuses it right after the handshake when
  the ALPN is `axiom-mesh/1`: closed before a protocol byte, but after a completed handshake. Outside a pairing nothing
  changes: an unknown key is refused inside the handshake.
- **Evidence:** probed on 2026-10-29 on Windows 11: `SslStream.NegotiatedApplicationProtocol` read inside the server's
  certificate callback throws `InvalidOperationException` on .NET 8.0.32 and answers on .NET 10.0.13; Linux (OpenSSL) not
  probed. `Axiom.Mesh.Core/Transport/PeerCheck.cs` (`NegotiatedSoFar`, `Decide`);
  `HandshakeTests.UnderTheMeshAlpnAnUnknownKeyIsRefusedWhileAPairingWaits_UnderThePairingAlpnItIsLetThrough`.
- **Tolerated because:** no AMP/1 byte reaches such a peer, the window is open only while a pairing waits (at most 10
  minutes, one at a time), and the design's alternative, a second port during pairing, contradicts PAIR-SPEC.md §4.1
  (the code's addresses are the mesh listener). Where the callback can read the ALPN the refusal stays inside the handshake.
- **Paid in:** not planned; it disappears when net8.0 is dropped, if Linux behaves like .NET 10 on Windows (probe in M11 on
  the Linux runner).

## 22. A pairing connection reaches the AMP/1 handler

- **Rule:** DESIGN.md §6.3 and PAIR-SPEC.md §5.1: a pairing connection speaks `axiom-mesh-pair/1` from its first frame.
- **Instead:** the mesh port has one entry protocol, AMP/1, so a connection under the pairing ALPN opens an AMP/1 session;
  `PeerSessionHandler` aborts it at once, before any frame, because the pairing shell is not built.
- **Evidence:** `Axiom.Mesh.Core/Node/PeerSessionHandler.cs` (`IsPairing` → `connection.Abort()`).
- **Tolerated because:** no pairing exists before M10, and nothing is sent on such a connection.
- **Paid in:** M10. LWSE's `ConnectionContext.SwitchAsync` sends a final message of the old protocol, which AMP/1 must not
  send here; M10 serves the pairing session from the handler with `PairProtocol.Definition` on the connection's stream, or
  asks LWSE for a switch without a final message.

## 23. The peer table is not persisted

- **Rule:** SPEC.md §12.1: a device that caused three violations within an hour SHOULD NOT be dialled for an hour; §4.4's
  backoff is per address.
- **Instead:** backoffs, failures and violations live in memory and start empty after a restart; the `Peers` table of the
  index (`PeerRow`) is not written yet.
- **Evidence:** `Axiom.Mesh.Core/Peers/PeerTable.cs`; `Axiom.Mesh.Core/Storage/PeerRow.cs`.
- **Tolerated because:** both are SHOULDs and a restart is rare; a hostile peer still costs one handshake per attempt.
- **Paid in:** M9, with the peers requests (`ListPeers`, static peers), which read and write `PeerRow`.

## 24. An established session answers nothing yet

Paid in e6a8f57 (M7): `PeerSessionHandler` hands the established session to a `ReplicationSession`, which lists, wants,
answers `Want` and `RosterQuery`, takes objects and pushes changes. The `Hello` still carries no capability flag:
following new Packages automatically (bit 0) is entry 30.

## 25. A node takes its device key at start only

Paid in 2c46710 (M8): `CreateIdentity` on a running node makes the device key (unless the key store holds one) and the
user key from the node's `IEntropy`, writes both to the key store, swaps the device credential and the Entry writer in
through `Transport/CredentialSlot.cs` (handshakes read it lock-free from then on), signs a Roster with seq 1 and adopts
it (`IdentityRequestTests.CreateIdentityMakesRosterSeq1WithThisDevice_TwiceIsAlreadyExists`,
`Host.Tests: RealPipeTests`). `ShowDevice` enrolment uses the same slot in M10.

## 26. A Grant withdrawn by an owner's listing is withdrawn in memory only

- **Rule:** SPEC.md §7.2: a node whose user is not the owner, receiving from a device of the owner a listing that omits a
  Package it listed before, MUST treat its own Grant for that Package as withdrawn until a newer Grant proves otherwise.
- **Instead:** the withdrawal is a mark on the trust the writer holds (`PackageTrust.WithdrawnGrant`), not a row: a
  restart, or a batch that fails and makes the writer reload, forgets it until the owner's next listing omits the
  Package again.
- **Evidence:** `Axiom.Mesh.Core/Trust/TrustBuilder.cs` (`WithdrawOwnGrant`), `Store/StoreBatch.cs` (`Withdraw`);
  `ListingTests.AnOwnersListingThatOmitsAPackageWithdrawsTheGrantUntilANewerGrantProvesOtherwise`.
- **Tolerated because:** every session with a device of the owner starts with a listing, so the mark returns with the
  first one; meanwhile the node lists the Package to its own other devices only, which see the same owner.
- **Paid in:** M9, with the grants requests (`ListGrants` shows it), as a column of `GrantRow` and a schema version step.

## 27. Objects do not stream, and forwarding one allocates more than its copies

- **Rule:** plan §3.5 and the M7 brief: objects go from the store into the writer in pooled 64 KiB slices, with no whole
  copy on the send path; intake copies a frame once into a pooled buffer.
- **Instead:** ProtoStream 0.2.0's frame-codec route encodes a whole message into its pooled write buffer and has no
  payload streaming, so an object is read from the store into one pooled buffer of its size and handed to
  `ObjectMessage.FromStored`. Forwarding 16 objects of 1 MiB between two in-memory nodes allocates 4.2 to 4.7 MiB per
  object (net8.0 and net10.0, Windows): 2 MiB are the two in-memory stores' own copies, intake stays within its pooled
  copy, and the remaining ~1.5 MiB per object lies in the TLS, pipe and frame layers, not yet attributed.
- **Evidence:** `Axiom.Mesh.Core/Replication/ReplicationSession.cs` (`SendObjectAsync`);
  `ReplicationRegressionTests.ForwardingAMebibyteObjectAllocatesNoFurtherWholeCopy` (a budget of 5 MiB per object).
- **Tolerated because:** nothing in it grows with more than one object at a time, the buffers on Mesh's own path are
  pooled, and the benchmarks that measure throughput come in M12.
- **Paid in:** M12, with the loopback benchmark (attribute the allocations; ask ProtoStream for a streamed payload on the
  frame-codec route if the send path needs it).

## 28. An Entry stored before its parents is not checked against them

- **Rule:** SPEC.md §8.6, §12.2: once its parents are held, an Entry whose parent is of another Package, whose clock is
  not above every parent's, or more than 2^32 above the greatest, is faulty and not forwarded.
- **Instead:** the writer checks an Entry when its parents are already stored. A history walk stores the newest Entry
  first and its parents after it; when they arrive, the stored child is not checked again.
- **Evidence:** `Axiom.Mesh.Core/Store/StoreBatch.cs` (`HasParentFaultAsync` returns no fault while a parent is missing).
- **Tolerated because:** such an Entry was signed by a device that may write (`EntryAdmission`), so a fault costs
  ordering, not trust; the clock gap rule's purpose, that a writer cannot sort last for ever, still holds for every
  Entry written after a held one.
- **Paid in:** M9, with the history requests: the writer checks the stored children of an Entry it stores (a reverse
  parent index) and marks a faulty one, which is then neither listed as a Head nor served.

## 29. History is walked when an object is stored, not when the policy changes

- **Rule:** SPEC.md §10.3, §11.2: a node wants the payload objects its retention policy asks for.
- **Instead:** the walk runs from each object as it is stored. A policy that grows later (`SetPolicy` to Everything, or
  a Package followed again) wants nothing for the Entries already stored; a change subscription that lapses loses the
  walks of the change sets it dropped until a peer lists the Package again.
- **Evidence:** `Axiom.Mesh.Core/Replication/Replicator.cs` (`WalkAsync`).
- **Tolerated because:** a desktop keeps everything from the start, and `Fetch` and `Materialise` (M9) fetch on demand
  through the same scheduler.
- **Paid in:** M9, with `SetPolicy`, `Fetch` and `Materialise` (a walk over the stored Entries of a Package).

## 30. Optional replication behaviours are not offered

- **Rule:** SPEC.md §7.6: a node SHOULD send `Have` for hashes it holds but has deferred answering; §10.2: a node that
  follows new Packages automatically (capability bit 0) MAY start following a Package it is granted.
- **Instead:** deferred answers are not announced with `Have`, and `Hello` carries no capability flag.
- **Evidence:** `Axiom.Mesh.Core/Replication/SessionOutbox.cs`, `Node/PeerSessionHandler.cs` (`OwnHello`).
- **Tolerated because:** both are SHOULD or MAY; a deferred answer is sent once the queue drains, and following is the
  user's decision in phase 1.
- **Paid in:** phase 1b, with the Sync service and device policies (DESIGN.md §17).

## 31. Transfers are not recorded for the local protocol

- **Rule:** LOCAL-SPEC.md §5.16 and the `transfers` topic: every object wanted, received, sent, not found, cancelled or
  dropped is a `Transfer` event.
- **Instead:** the node records facts (`ObjectStored`, `WantAnswered`, `FaultyContent`) and counters
  (`MeshNode.Replication`), but no `Transfer` records.
- **Evidence:** `Axiom.Mesh.Core/Replication/ReplicationSession.cs`, `WantScheduler.cs`.
- **Tolerated because:** the `transfers` topic is subscribable since M8 and simply carries no events yet; no client
  needs transfers before the CLI's progress output and the app.
- **Paid in:** M9, with `Fetch` (a transfer sink the scheduler and the sessions write to, read by the local port's event
  hub).

## 32. Spawned services are configured, not spawned

- **Rule:** LOCAL-SPEC.md §4.2: for a service the Host spawns, it puts a fresh token into the child's environment and
  supervises the child.
- **Instead:** `config.json` accepts `services.<name>.spawn`, but the daemon starts no process and gives no token, so
  such a name is refused at `Hello` until a later version spawns it; the daemon logs a configuration note at start.
  `LocalService` already carries a spawned service's token, and the `Hello` check compares it in constant time.
- **Evidence:** `Axiom.Mesh.Host/HostRuntime.cs` (`Note`), `Axiom.Mesh.Host/Configuration/HostConfiguration.cs`
  (`ServiceOf`); `LocalHelloTests.Refusals` (wrong spawned token).
- **Tolerated because:** no first-party service runs out of process in phase 1a; the CLI and scripts are external.
- **Paid in:** phase 1b, with the first service that runs as its own program (process supervision and restart).

## 33. Package events are built from the batch, not from a diff of every field

- **Rule:** LOCAL-SPEC.md §7.3, §5.4: `PackageChanged` carries the Package after the change, the Grant for change Grant,
  the Head for change Head; `Package.listedBy` counts the established peers that list it.
- **Instead:** a Grant change names the Grants that differ from the previous trust; a Head change names the Heads of
  the devices whose Entries the batch stored (a Head that changed because an Entry was evicted is not announced, entry
  18); `listedBy` is 0; the count of stored Entries is read from the index for each event.
- **Evidence:** `Axiom.Mesh.Core/Local/LocalEventHub.cs` (`EventsOf`), `Local/PackageRecords.cs`.
- **Tolerated because:** a client that needs the exact state calls `GetPackage` (M9) after an event, which §3.4 advises
  anyway; no listing count is shown before the app.
- **Paid in:** M9, with `GetPackage` and `ListPackages` (one record builder for requests and events, `listedBy` from the
  sessions' listings, the Entry count kept per Package).

## 34. ConfirmPairing is a user-key request in every mode

- **Rule:** LOCAL-SPEC.md §2.2: `ConfirmPairing` is a user-key request in modes `ShowUser`, `JoinUser` and `AddDevice`.
- **Instead:** the dispatcher marks `ConfirmPairing` user-key whatever the mode (`ShowDevice` included), because the
  mode belongs to the running pairing, which does not exist before M10.
- **Evidence:** `Axiom.Mesh.Core/Local/RequestRoutes.cs` (`SpendingUserKey`).
- **Tolerated because:** it can only log or confine one request too many, never one too few.
- **Paid in:** M10, with the pairing shell (the handler decides the mode and the confinement).

## 35. An event's size is counted by encoding it once more

- **Rule:** fundamentals plan §2.5, M8 brief: events are forwarded without re-encoding.
- **Instead:** each event is made once and shared by every subscription and connection that receives it, but its frame
  size (for the 16 MiB and 64 MiB bounds) is counted by writing it into a counting buffer once, and each connection then
  encodes it again when it writes the frame. Responses are counted the same way.
- **Evidence:** `Axiom.Mesh.Core/Local/FrameSizes.cs`, `Local/CountingBufferWriter.cs`.
- **Tolerated because:** the count costs one pass over an event that is written at least once anyway, and nothing is
  allocated beyond a pooled scratch buffer; a pre-encoded event needs an API in `Axiom.Mesh.Local`.
- **Paid in:** M12, with the benchmarks (measure; if it matters, a pre-encoded `EventFrame` in Local).

## 36. ListDiscovered lists what the discovery cache holds, not the last hour

- **Rule:** LOCAL-SPEC.md §6.6: `ListDiscovered` lists every device discovery saw in the last hour.
- **Instead:** it lists the peer table's devices and the devices the bounded discovery cache holds, whenever heard.
- **Evidence:** `Axiom.Mesh.Core/Local/NodeRequests.cs` (`ListDiscoveredAsync`).
- **Tolerated because:** the cache is bounded (1 024 devices on the Host) and a LAN rarely holds more; the hour needs a
  timestamp per claim the cache does not keep.
- **Paid in:** M9, with the peers requests (a time per claim in `AnnouncementCache`, or a seen-at in the peer table).

## 37. Another operating-system user is refused end to end only in a unit test

- **Rule:** LOCAL-SPEC.md §15: the pipe admitted for the Host's OS user and refused for another, by access list and by
  process identity, on each desktop platform.
- **Instead:** the decision is tested (`PipeAdmissionTests`) and the served user end to end on the real pipe
  (`RealPipeTests`), but no test connects as another OS user: that needs a second account (Windows) or `sudo -u nobody`
  (Linux), which a unit test cannot assume.
- **Evidence:** `UnitTests/Axiom.Mesh.Host.Tests/Rules/RealPipeTests.cs`; CONFORMANCE.md (LOCAL §2.2, planned row).
- **Tolerated because:** the access list (owner only) and the process identity check are independent fences, each
  checked by construction and by the decision's tests.
- **Paid in:** M11, with the integration tests (`PipeTests.AnotherUserIsRefusedByTheAccessListAndByProcessIdentity`,
  skipped where no second account or sudo exists).
