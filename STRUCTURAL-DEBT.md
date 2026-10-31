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
  The integration suite (M11) runs real nodes on sockets but announces their endpoints on an in-process board, the claims
  mDNS would carry (`IntegrationTests/.../Support/RealNode.cs`); a static peer added over the command line is dialled on
  real sockets (`Integration.Tests: StaticPeerTests`).
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

- **Rule:** LOCAL-SPEC.md §6.7, §9.8 and §9.10 (`ServiceCall`, `ForwardedCall`, `Emit`).
- **Instead:** those requests answer `UnknownRequest`. The machinery under them exists since M9: a callback table per
  connection (64 outstanding, deadlines, late replies ignored, unknown ids a violation) with `LocalConnection.CallAsync`,
  and `LocalEventHub.ServiceEmitted` for topic `service`; nothing calls either yet.
- **Evidence:** fundamentals plan, D7; `Axiom.Mesh.Core/Local/CallbackTable.cs`, `RequestRoutes.cs`.
- **Tolerated because:** no service with a namespace exists in phase 1a; `UnknownRequest` keeps the Host conformant.
- **Paid in:** phase 2 (Chat service), the first service with a namespace: two handlers over the existing machinery.

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
- **Instead:** a pairing code is a text string; `axiom-mesh pair show` prints it, and on a terminal also in a box that
  is easy to read off. No QR code is drawn yet (an ASCII QR encoder is a later step), and no six-word form exists.
- **Evidence:** fundamentals plan, D7; `Apps/Axiom.Mesh.Cli/Output/PairingView.cs` (`Box`).
- **Tolerated because:** desktops can copy text; a camera is a phone's.
- **Paid in:** phase 4 (Android, iOS).

## 12. The Host answers only the phase-1 request subset

- **Rule:** LOCAL-SPEC.md §15: every request of §6–§10 answered with its OK body and every listed error.
- **Instead:** M8 and M9 serve the phase-1 set of the fundamentals plan §2.5 except pairing; `StartPairing`,
  `ConfirmPairing`, `CancelPairing` and `GetPairing` (M10), `UpdatePackage`, `ServiceCall`, `AddTelemetry` and `Emit`
  answer `UnknownRequest`; the telemetry queries answer `NotFound` with Host capability bit 2 clear
  (`Axiom.Mesh.Core/Local/RequestRoutes.cs`).
- **Evidence:** fundamentals plan §2.5 and D7.
- **Tolerated because:** `UnknownRequest` is the specification's answer for a request a Host does not serve, so a
  half-built Host stays conformant.
- **Paid in:** M10 (pairing) and the phases that need the rest (entries 5, 6 and 7 above).

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

Paid in a679b50 (M10): the mesh port still has one entry protocol, but AMP/1's Connected state is switchable, and
`PeerSessionHandler` hands a connection under the pairing ALPN to `PairingShell.ServeAsync`, which switches the AMP/1
session that has sent and read nothing to `PairProtocol.Definition` with ProtoStream's `SwitchAsync` without a final
message; `PairHello` is the connection's first frame, and nothing follows the pairing's end
(`Core.Tests: PairingTests.APairingConnectionCarriesNoAmp1Frame_AndASecondOneIsAnsweredBusy`). A pairing connection that
reaches a node with no pairing at all is still aborted without a byte.

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
- **Not paid in M9:** `WriteEntry` never stores before its parents (it names stored parents only), so the request work
  did not reach this path; the check belongs to the history walk.
- **Not paid in M11:** SPEC.md draft 4 now states the rule (§8.6: checked when the last parent is stored; a faulty one no
  longer listed, served or forwarded). Doing it needs a reverse parent index (a schema step) and a way to retire a stored
  Entry from Heads, pins and serving, which is entry 18's missing Head election as well; the scenario suite's walks never
  meet a faulty late parent, so nothing proved the shape of either.
- **Paid in:** M12, together with entry 18: a `ParentRow` index written with each Entry, the children of a newly stored
  Entry checked against `Lamport.TryFindParentFault`, a faulty one retired as an eviction that elects the next Head.

## 29. History is walked when an object is stored, not when the policy changes

- **Rule:** SPEC.md §10.3, §11.2: a node wants the payload objects its retention policy asks for.
- **Instead:** the walk runs from each object as it is stored. A policy that grows later (`SetPolicy` to Everything, or
  a Package followed again) wants nothing for the Entries already stored; a change subscription that lapses loses the
  walks of the change sets it dropped until a peer lists the Package again.
- **Evidence:** `Axiom.Mesh.Core/Replication/Replicator.cs` (`WalkAsync`).
- **Tolerated because:** a desktop keeps everything from the start, and since M9 `Fetch` and `Materialise` fetch on
  demand through the same scheduler, which covers what a grown policy would want.
- **Paid in:** phase 1b, with the Sync service's device policies (a walk over the stored Entries of a Package when its
  policy grows).

## 30. Optional replication behaviours are not offered

- **Rule:** SPEC.md §7.6: a node SHOULD send `Have` for hashes it holds but has deferred answering; §10.2: a node that
  follows new Packages automatically (capability bit 0) MAY start following a Package it is granted.
- **Instead:** deferred answers are not announced with `Have`, and `Hello` carries no capability flag.
- **Evidence:** `Axiom.Mesh.Core/Replication/SessionOutbox.cs`, `Node/PeerSessionHandler.cs` (`OwnHello`).
- **Tolerated because:** both are SHOULD or MAY; a deferred answer is sent once the queue drains, and following is the
  user's decision in phase 1.
- **Paid in:** phase 1b, with the Sync service and device policies (DESIGN.md §17).

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
  the Head for change Head.
- **Instead:** a Grant change names the Grants that differ from the previous trust; a Head change names the Heads of
  the devices whose Entries the batch stored (a Head that changed because an Entry was evicted is not announced, entry
  18); the count of stored Entries is read from the index for each record. Since M9 one builder
  (`PackageRecords.BuildAsync`) makes the record for `GetPackage`, `ListPackages`, the Package requests and the events,
  and `listedBy` counts the established sessions whose current listing names the Package.
- **Evidence:** `Axiom.Mesh.Core/Local/LocalEventHub.cs` (`EventsOf`), `Local/PackageRecords.cs`.
- **Tolerated because:** a client that needs the exact state calls `GetPackage` after an event, which §3.4 advises
  anyway; a peer listing or delisting a Package changes `listedBy` without an event of its own, which §14.12 does not ask
  for.
- **Paid in:** M12, with the performance pass (an Entry count kept per Package instead of a count query per record).

## 34. ConfirmPairing is a user-key request in every mode

- **Rule:** LOCAL-SPEC.md §2.2: `ConfirmPairing` is a user-key request in modes `ShowUser`, `JoinUser` and `AddDevice`.
- **Instead:** the dispatcher marks `ConfirmPairing` user-key whatever the mode (`ShowDevice` included), because the
  mode belongs to the running pairing, which does not exist before M10.
- **Evidence:** `Axiom.Mesh.Core/Local/RequestRoutes.cs` (`SpendingUserKey`).
- **Tolerated because:** it can only log or confine one request too many, never one too few. M10 built the pairing
  shell but kept the static table: confining by mode needs the dispatcher to read the running pairing's mode before the
  handler, a change of `DispatchRule` that is not worth its risk for one request of a device that holds no user key.
- **Paid in:** M12, with the security conformance pass (the dispatcher asks the pairing shell for the mode).

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
- **Instead:** since M11 Linux is proved end to end: `Integration.Tests: PipeTests.OnLinuxAnotherUserIsRefusedByTheSocketsModeAndByProcessIdentity`
  connects as user `nobody` (`sudo -n -u nobody`, password-less on GitHub's Ubuntu runners; skipped with the reason where
  it is not) and is refused by the socket's 0600 mode, and with the mode opened up, by the `SO_PEERCRED` check. Windows is
  proved by its two fences separately (`OnWindowsThePipesAccessListAllowsTheHostsAccountAlone`,
  `OnWindowsAConnectionThroughTheMachinesNameIsRefused`); a connection as another Windows account needs that account's
  password (`LogonUser`) or elevation, and is skipped with that reason.
- **Evidence:** `UnitTests/Axiom.Mesh.Host.Tests/Rules/RealPipeTests.cs`; CONFORMANCE.md (LOCAL §2.2, planned row).
- **Tolerated because:** the access list (owner only) and the process identity check are independent fences, each
  checked by construction and by the decision's tests.
- **Paid in:** M13, narrowed to Windows: the install-script CI job (which runs as an administrator on the runner) creates
  a second local account and connects as it.

## 38. A budget set by request is not kept across a restart

- **Rule:** LOCAL-SPEC.md §6.5 `SetStoreBudget`: the budget from now on.
- **Instead:** the writer holds the new budget in memory; at the next start the configured `store.budgetBytes` applies
  again.
- **Evidence:** `Axiom.Mesh.Core/Store/Commands/BudgetCommand.cs`, `Store/WriterState.cs` (`BudgetBytes`).
- **Tolerated because:** the configuration is the user's file and the request's effect is visible at once; nothing is
  lost, eviction only waits for the next pass under the configured budget.
- **Paid in:** M10, with the CLI's `store` command (the budget written to `config.json` or the index, one place).

## 39. ListPins without a filter reads every pin

- **Rule:** ARCHITECTURE.md rule 6: every wait has a deadline and every queue a bound; a page costs a few index reads.
- **Instead:** pins are ordered by their 65-byte key, which LDB 0.1.0 sorts in memory; `ListPins` with a hash or a
  Package reads only the matching rows, without a filter it reads every pin and orders them for each page.
- **Evidence:** `Axiom.Mesh.Core/Local/PinOrder.cs`, `Local/StoreRequests.cs` (`ListPinsAsync`).
- **Tolerated because:** a desktop store holds thousands of pins, which sort in milliseconds; clients list pins of one
  object or one Package.
- **Paid in:** M12, with the performance pass (a keyset page over an ordered key, as `EntryIndex` does for Entries).

## 40. Paths are opened as the Host's user, not the client's

- **Rule:** LOCAL-SPEC.md §13: `Materialise`, `PutFile` and `GetStorePath` open paths as the user of the connection that
  named them (Windows: impersonation around every open, create, delete and rename).
- **Instead:** the Host opens them as itself. Host capability bit 1 tells a client of another OS user that the Host runs
  under another account, and bit 0 is clear for it.
- **Evidence:** `Axiom.Mesh.Core/Local/MaterialiseWork.cs`, `Local/PutFileWork.cs`.
- **Tolerated because:** in phase 1a the Host runs as the one user it serves (the pipe admits that user only, entry 3),
  so the Host's user and the client's are the same.
- **Paid in:** with the system-service mode of entry 10 (impersonation on Windows, a per-user Host on Unix).

## 41. An unfollowed Package that no peer lists stays Listed

- **Rule:** LOCAL-SPEC.md §6.4: `UnfollowPackage` returns the record to Listed while a peer lists it, else it is dropped.
- **Instead:** the record returns to Listed whatever the listings say.
- **Evidence:** `Axiom.Mesh.Core/Store/StoreBatch.cs` (`PackageAsync`), `Local/PackageRequests.cs`.
- **Tolerated because:** a Listed record pins nothing and costs a row; following it again is the user's choice.
- **Not paid in M11:** dropping the record means a Package of trust state `None` that may still have Entries in the
  store (unpinned, evictable); what `ListPackages`, visibility and a later listing then make of those Entries is not
  specified, and no scenario needed the drop.
- **Paid in:** M12: drop the record when the last session that listed it omits it and nothing of it is pinned, evict its
  Entries with it, and say so in LOCAL-SPEC.md §6.4.

## 42. Fetch and Materialise do not check that the Package references the object

- **Rule:** LOCAL-SPEC.md §6.4: `packageId` is a followed Package whose Entries reference the object.
- **Instead:** the Host checks that the Package is followed and asks the peers that list it; a peer resolves visibility
  (SPEC.md §9.7) and answers `NotFound` for an object the Package does not reference, so the operation ends `NotFound`
  after 60 seconds without a source.
- **Evidence:** `Axiom.Mesh.Core/Local/OperationStarts.cs`, `Local/ObjectFetcher.cs`.
- **Tolerated because:** nothing is served that the peer would not serve anyway; the cost is a slower `NotFound`.
- **Paid in:** M12 (an upward walk over the reference rows before the operation starts).

## 43. A Fetch notices a new source on its next look, once a second

- **Rule:** ARCHITECTURE.md rule 6 and SPEC.md §10.5: no polling.
- **Instead:** a waiting Fetch looks once a second on the node's clock whether a session now lists the Package, besides
  waking for every change set; a session that lists the Package changes nothing in the store, so it has no event.
- **Evidence:** `Axiom.Mesh.Core/Local/ObjectFetcher.cs` (`Look`).
- **Tolerated because:** one timer per waiting operation, at most 16 per connection; the 60-second rule needs a clock
  anyway.
- **Paid in:** M12 (a listing event from the sessions to the scheduler).

## 44. Forgetting a user does not announce its devices as forgotten

Paid in M11: `ForgetUser` removes the user's devices from the peer table (`PeerTable.ForgetUser`), so topic `peers`
reports each as `Forgotten` (`Core.Tests: UserRequestTests.ForgettingAUserForgetsItsDevicesAsPeers_TopicPeersSaysForgotten`).

## 45. A paired user's display name is not kept

Paid in M11: `UserRow.Name` (index schema version unchanged, no release exists yet) keeps the name a pairing showed;
`ListKnownUsers` and `UserChanged` read it; `MeshNode.AdoptIdentityAsync(roster, userName, ct)` gives a node's identity a
name (`Testing.Tests: MeshScenarios.TwoUsersWhoPairSeeTheSameCode_KnowEachOtherByName_AndEstablishOneSessionEach`).

## 46. The entering side dials only the IP addresses of a code

Paid in M11: `IDialer.ResolveAsync(host)` (the system resolver in `TcpDialer`, `InMemoryNetwork.Name` in memory) turns a
host name of a code into addresses before admission decides on each (`Core.Tests: PairingTests.ACodeThatNamesAHostIsResolvedAndDialled`).

## 47. The command line is compiled ahead of time without the IL scanner

- **Rule:** ARCHITECTURE.md §10: every library and executable runs trimmed and ahead of time, warnings as errors.
- **Instead:** ILCompiler 10.0.10 fails generating `Dictionary<long, __Canon>.CopyTo` (reached through ProtoStream's
  message set codec) from a generic dictionary layout its IL scanner precomputed; `Axiom.Mesh.Cli.csproj` passes
  `--noscan` to the compiler when `PublishAot` is set. The result is correct, a little larger and slower to start.
- **Evidence:** `Apps/Axiom.Mesh.Cli/Axiom.Mesh.Cli.csproj` (`IlcArg`); `Scripts/aot-smoke.ps1` passes with it.
- **Tolerated because:** it is a compiler defect, not one of this code; the Host compiles with the scanner.
- **Paid in:** M12, with `aot.yml`: drop the argument once a patched ILCompiler compiles the command line.

## 48. The command line does not read `pipe.name` from `config.json`

- **Rule:** the Host's pipe name precedence: `AXIOM_MESH_PIPE`, then `pipe.name` in `config.json`, then the default.
- **Instead:** the command line finds the pipe by `AXIOM_MESH_PIPE` or the platform default (`LocalPipe.Locate`); a Host
  configured with another `pipe.name` is reached only with the variable set.
- **Evidence:** `Apps/Axiom.Mesh.Cli/Program.cs` (`Locate`).
- **Tolerated because:** `pipe.name` is rare outside system services; the failure line names the pipe it tried.
- **Paid in:** M13, with the install scripts, which set up the pipe the command line expects.

## 49. A pairing code lists every interface's address, reachable or not

Paid in M11: `PairingAddressRule` lists the addresses of interfaces that are up, routable before link-local before
loopback (each only when the tier before is empty), interfaces with a gateway first, IPv4 before IPv6; a node whose
listener is bound to one address (`MeshNodeOptions.ListenAddress`, the Host's `mesh.listen`) lists that address alone
(`Core.Tests: PairingAddressRuleTests`).

## 50. The scripted peer can write a frame start beside its session

- **Rule:** one writer per TLS stream (an `SslStream` takes one write at a time; AMP/1's writes are serialised by the
  ProtoStream connection's write lock, the heartbeat and automatic answers included).
- **Instead:** `ScriptedPeer.SendFrameStartAsync` (the harness, not the product) writes a partial frame straight to the TLS
  stream while its session is open, outside that lock; a heartbeat or an automatic `Pong` of the session at the same
  moment would make `SslStream` refuse the second write. `SendRawAsync` refuses to run with a session open.
- **Evidence:** `Axiom.Mesh.Testing/ScriptedPeer.cs`. Every write of Core goes through `Session.WriteAsync` (M11 review
  of `PeerSessionHandler`, `ReplicationSession`, `PairingShell`, `ActivityStream`, LWSE's in-memory transport).
- **Tolerated because:** the one test that uses it writes after the session has been silent for less than 60 seconds,
  when the session sends nothing on its own.
- **Paid in:** M12, by asking ProtoStream for a raw write under the connection's lock (or a partial-frame message type).

## 51. The socket fixture keeps its nodes' keys in memory

- **Rule:** ARCHITECTURE.md rule 3 (keys only in the Host's key store) as the shipped daemon applies it.
- **Instead:** `RealMeshFixture` gives each real node an `InMemoryKeyStore` that outlives its restarts; the Host's
  `FileKeyStore` is internal to the Host and covered by `Host.Tests` (`FileKeyStoreTests`, `RealPipeTests`).
- **Evidence:** `IntegrationTests/Axiom.Mesh.Integration.Tests/Support/RealNode.cs`.
- **Tolerated because:** the scenarios test replication, trust and the pipe, not key storage.
- **Paid in:** M13, with the install-script job that runs the published daemon end to end.

## 52. A history walk asks for one parent per round trip

- **Rule:** fundamentals plan §5: a 10 000-entry history walk within 10 s (benchmark target; the release floor is 120 s).
- **Instead:** M11's floor measured 51.5 s on loopback with TLS (194 Entries/s, Windows, net10.0): a walk wants an Entry's
  parents only once the Entry is stored, so a linear Channel costs one round trip and one store batch per Entry.
- **Evidence:** `Integration.Tests: FloorTests.AHistoryOfTenThousandEntriesIsWalkedWithinTwoMinutes` (run with
  `AXIOM_MESH_FLOORS=1`).
- **Tolerated because:** the floor holds, and a follower catching up on a long Channel is rare in phase 1.
- **Paid in:** M12, with the benchmark: batch the walk (ask for the parents of every Entry of a stored batch at once) and
  measure against the 10 s target.

## 53. A cancelled read can leave the next write encrypted and refused

- **Rule:** ARCHITECTURE.md rule 6 (every wait a deadline) without corrupting the stream it bounds.
- **Instead:** ProtoStream 0.2.0 flushes a session's automatic answers (a `Pong`) under the token of the read that
  received the `Ping`; a read cancelled just as that flush ends leaves `StreamPipeWriter`'s internal cancellation set, and
  the next write hands an already-cancelled token to `SslStream`, which encrypts the record and then has it refused by
  the transport: the record sequence has a gap and the peer reports "decryption failed". M11 found this as the flaky
  `ListingTests("61 seconds")` (about one run in twelve of a 400-iteration stress, none in 1 600 after the fix).
- **Paid in M11 for the violations a session raises outside the read** (a listing too slow, a frame not completed):
  `ReplicationSession` no longer cancels its read; it abandons it and writes `Bye(Violation)` beside it. Writes of the
  write loop and of the Hello exchange are never cancelled once begun.
- **Still open:** the drain path (`Bye(Shutdown)` after the read is cancelled by the stopping token) and the frame bound
  before `Hello` keep the pattern; a lost `Bye` there costs nothing but the reason.
- **Evidence:** `Axiom.Mesh.Core/Replication/ReplicationSession.cs` (`RunAsync`); ProtoStream `Session.cs` (automatic reply
  under the read's token), `StreamPipeWriter` (cancellation kept for the next flush).
- **Paid in:** M12, upstream: ProtoStream flushes automatic answers without the read's token (or resets the writer's
  cancellation before a write), after which the drain path needs no change.
