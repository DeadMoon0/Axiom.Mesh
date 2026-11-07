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

Paid in M13: [Documentation/PIPE-ACCESS.md](Documentation/PIPE-ACCESS.md) says who may open the pipe on each desktop
platform (the access list, the socket mode, the remote-client and first-instance flags), how the Host decides on a
connection that reached it (`PipeAdmission.Decide` over the process identity), what `pipe.allowUsers` does and does not
do today, how the pipe is named (`AXIOM_MESH_PIPE`, `pipe.name`, the default), and which proofs exist per platform
(entry 37).

## 4. KEY-BACKUP.md is not written, and the user key has no backup

- **Rule:** LOCAL-SPEC.md §6.1 specifies the user key's backup apart, in `KEY-BACKUP.md`.
- **Instead:** neither the document nor the export/import requests exist; losing the device that holds the user key
  loses the user's identity.
- **Evidence:** fundamentals plan, decision D7.
- **Tolerated because:** phase 1a is desktop only and accepts re-creating an identity.
- **Paid in:** a later phase (DESIGN.md §17, "later").

## 5. Branch leaves, the merge table and conflict names are not built

Paid in S2 (the sync service): the leaf rule, the display head, the common ancestor (criss-cross: the highest, no
recursion), the merge table path by path with directory and file meeting as a conflict whose loser's subtree lies beside,
the chain of two-parent merges and conflict names with `-2`, `-3` are pure rules of `Services/Axiom.Mesh.Sync/Rules`
(`Leaves`, `Ancestor`, `Merge`, `ConflictName`, `SyncPlanner`), applied by the service to an Archive's Versions; the owner's
Manifest `branches` are shown only. Tests: `Sync.Tests: LeafTests`, `AncestorTests`, `MergeTableTests`, `ConflictNameTests`,
`PlannerTests`, `SyncScenarios`; the CONFORMANCE row of SPEC §9.5 is no longer deferred.

## 6. Telemetry series are not recorded

- **Rule:** LOCAL-SPEC.md §10.
- **Instead:** the telemetry requests answer `NotFound` and Host capability bit 2 is clear; counters go to LWSE's
  meter behind a sink only.
- **Evidence:** fundamentals plan, D7 and §3.7.
- **Tolerated because:** the requests answer as the specification allows, and nothing in phase 1a reads the series.
- **Paid in:** phase 3 (the app's mesh analytics).

## 7. Service calls and emitted events are not built

Paid in S0 (phase 1b plumbing): `ServiceCall` is forwarded as a `ForwardedCall` and answered by the pure
`ForwardedReplyRule` (`ServiceUnavailable` at once, `ServiceTimeout` at 60 s, `Busy` past 64, a reply the Host cannot pass
on a violation of the service's connection), outside the caller's ordered lane; `Emit` publishes `ServiceEvent`s;
`CallbackTable` ends each callback as a `CallbackOutcome`; the client side serves callbacks off its read loop
(`CallbackHandler`, `CallbackDispatch`). Tests: `Core.Tests: ServiceCallTests`, `EmitTests`, `ServiceCallLaneTests`;
`Local.Tests: CallbackServingTests`, `CallbackReplyOnceTests`.

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

## 10. The Host starts at the user's logon, not as a system service

- **Rule:** DESIGN.md §10.5 (how the Host runs per platform).
- **Instead:** since M13 the install scripts and `axiom-mesh host install` register the Host to start at the user's
  logon (a per-user scheduled task on Windows, a systemd user unit on Linux; fundamentals plan §6e), which is what a
  desktop needs. A Host that runs before anyone logs on, as a Windows service or a system unit serving one user, is not
  automated, and `pipe.allowUsers` (entry 3, PIPE-ACCESS.md) has no effect until it is.
- **Evidence:** `Apps/Axiom.Mesh.Cli/Autostart/ScheduledTaskPlan.cs`, `SystemdUnitPlan.cs`; `install.ps1`, `install.sh`;
  `Cli.Tests: AutostartPlanTests`, `HostInstallTests`; `.github/workflows/install-tests.yml`.
- **Tolerated because:** a desktop's user is logged on whenever the mesh matters to them; a headless node can run
  `axiom-meshd run` from any supervisor today.
- **Paid in:** phase 3, with the desktop app's installer, or earlier for a headless node if a user asks.

## 11. Pairing codes are text, not QR codes or six words

- **Rule:** DESIGN.md §5.2 and §17 phase 4 ("a phone pairs by QR code").
- **Instead:** a pairing code is a text string; `axiom-mesh pair show` prints it, and on a terminal also in a box that
  is easy to read off. No QR code is drawn yet (an ASCII QR encoder is a later step), and no six-word form exists.
- **Evidence:** fundamentals plan, D7; `Apps/Axiom.Mesh.Cli/Output/PairingView.cs` (`Box`).
- **Tolerated because:** desktops can copy text; a camera is a phone's.
- **Paid in:** phase 4 (Android, iOS).

## 12. The Host answers only the phase-1 request subset

- **Rule:** LOCAL-SPEC.md §15: every request of §6–§10 answered with its OK body and every listed error.
- **Instead:** narrowed in S0: every request of LOCAL-SPEC.md §14.1 has a handler (pairing since M10; `UpdatePackage`,
  `ServiceCall`, `Emit` and `AddTelemetry` since S0). What is left is telemetry: `ListSeries`, `ListKeys`, `GetTotal`,
  `QueryTelemetry` and `AddTelemetry` answer `NotFound` with Host capability bit 2 clear
  (`Axiom.Mesh.Core/Local/TelemetryRequests.cs`), so a Host keeps no telemetry and the topic `telemetry` stays silent.
- **Evidence:** `RequestRoutes.Handlers()`; `Core.Tests: EventTopicTests.TelemetryRequestsAreNotFoundOnAHostWithoutTelemetry`.
- **Tolerated because:** `NotFound` with bit 2 clear is the specification's answer for a Host without telemetry (§14.4);
  the analytics view it feeds is the app's (phase 3).
- **Paid in:** phase 3 (the app's mesh analytics, DESIGN.md §10.5), with the `Telemetry` tables of DESIGN.md §13.

## 13. The executables are named after their assemblies, not `axiom-meshd`

Paid in M12: a Native AOT publish sets `TargetName`, so the published executables are `axiom-meshd` and `axiom-mesh`
while the assemblies keep their package names for the layering conventions (`Axiom.Mesh.Host.csproj`,
`Axiom.Mesh.Cli.csproj`); `Scripts/aot-smoke.ps1` ran them so on win-x64, and since M13 `install-tests.yml` (which
replaced `aot.yml`) and `release.yml` publish, smoke and zip them on both platforms through `Scripts/package.*`. A
framework-dependent build (`dotnet run`, the tool package) keeps the assembly names.

## 14. The store directory is not flushed after a rename where .NET cannot open a directory

Paid in M13: .NET refuses to open a directory as a file stream on Unix (it checks `S_ISDIR` after `open` and throws), so
the old attempt was skipped on every Linux run. `DirectorySync` now opens the directory read-only through libc
(`open`, `fsync`, `close`, blittable P/Invokes as `LocalPipe` already uses for `getuid`) after every rename on Linux and
macOS; Windows needs no directory flush. `DirectoryObjectFiles.ProbeDirectoryFlush` reports `NotNeeded`, `Flushed` or
`Unsupported`, the daemon logs it at start (`store <path>: directory flush Flushed`), and two places prove it on Linux:
`Core.Tests: StoreTests.TheStoreDirectoryIsFlushedWhereThePlatformNeedsIt_AndNeedsNoFlushOnWindows` (on the Linux unit
job) and the install job, which greps the published daemon's log (`install-tests.yml`, step "The store directory is
flushed"). What no test can prove is that the disk honours `fsync`; that is the file system's promise. Both CI proofs
are new in M13 and have not run on a Linux machine yet (the development machine runs Windows).

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
- **Not paid in M9:** an earlier text said so, but no sweep was ever written; corrected in M13 after re-reading the
  code (`DirectoryObjectFiles` has no enumeration, `StoreWriter` no start-up pass).
- **Paid in:** phase 1b: a start-up sweep of `*.tmp-*` files and of object files the index does not name, bounded and
  run in the background before the first eviction pass.

## 17. Intake does not check an Entry's parents

Paid in e6a8f57 (M7): the store writer checks `Lamport.TryFindParentFault` against the stored parents before it stores
an Entry (`StoreBatch.HasParentFaultAsync`, `AdmitOutcome.Faulty`) and the session records the sender as faulty
(`FaultyContentTests`). What remains, an Entry stored before its parents, is entry 28.

## 18. Evicting an Entry forgets its Head without electing the next one

Paid in M12, with entry 28: when an evicted or retired Entry was its device's Head, the store writer elects the device's
next stored Entry by the head rule, writes its `Heads` row and `HeadsView`, and announces the change as a Head change
(`StoreBatch.ElectHeadAsync`; `Core.Tests: LateParentTests.EvictingADevicesHeadMakesItsNextStoredEntryTheHead`). A
dropped Package (entry 41) loses its Heads as a whole.

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
  `ObjectMessage.FromStored`. M12 attributed what forwarding allocates (`scenario forward` and `scenario loopback` of the
  benchmark suite, Documentation/BENCHMARKS.md): the frame layer (ProtoStream's encoder, decoder and pipes over a loopback
  TLS 1.3 stream, `SslStream` included) allocates about 53 KiB per 1 MiB object, Core's own buffers (intake's copy, the
  send buffer) are pooled and allocate nothing per object, and two real nodes allocate about 0.64 MiB per 1 MiB object in
  all, sampled by type as LDB's query and index arrays (`SlotRow[]`, `TreeEntry[]`) and byte arrays, from the index work
  each stored, served and checked object asks for. The ~1.5 MiB of M7's in-memory measurement was the in-memory stores and
  pipes. Core's part was cut in M12: the visibility walk remembers the whole path up from a visible Entry, so the Chunks of
  one ChunkList cost one upward walk instead of one each (`ReferenceWalk`, `ServedObjects`), and the want scheduler's
  settle checks no longer allocate a delegate per want.
- **Evidence:** `Benchmarks/Axiom.Mesh.Benchmarks/Scenarios/ForwardScenario.cs`, `LoopbackScenario.cs` with
  `AXIOM_MESH_ALLOCATIONS=1`; `ReplicationRegressionTests.ForwardingAMebibyteObjectAllocatesNoFurtherWholeCopy` (a budget
  of 5 MiB per object, in memory).
- **Tolerated because:** nothing in it grows with more than one object at a time, and the remainder belongs to the layers
  that own it: LDB (per-row query and index arrays) and ProtoStream (no streamed payload on the frame-codec route).
- **Paid in:** with the LDB release that pools its query buffers, and the ProtoStream release with a streamed payload on
  the frame-codec route; Core then reads objects into the writer in slices.

## 28. An Entry stored before its parents is not checked against them

Paid in M12, with entry 18: the store writer writes a `ParentRow` for each parent an Entry names that is not stored yet;
when that parent is stored, each child whose last missing parent it was is checked against `Lamport.TryFindParentFault`
from the index alone (a facts-based overload in Model), and one found faulty is retired: its pins dropped, its file and
rows evicted, its device's next stored Entry elected Head, so it is no longer listed, served or forwarded (SPEC.md §8.6
draft 5; `Core.Tests: LateParentTests`). What remains, recording the sender of such an Entry as faulty, is entry 59.

## 29. History is walked when an object is stored, not when the policy changes

- **Rule:** SPEC.md §10.3, §11.2: a node wants the payload objects its retention policy asks for.
- **Instead:** the walk runs from each object as it is stored. A policy that grows later (`SetPolicy` to Everything, or
  a Package followed again) wants nothing for the Entries already stored; a change subscription that lapses loses the
  walks of the change sets it dropped until a peer lists the Package again.
- **Evidence:** `Axiom.Mesh.Core/Replication/Replicator.cs` (`WalkAsync`).
- **Tolerated because:** a desktop keeps everything from the start, and since M9 `Fetch` fetches on demand through the
  same scheduler (the client's helpers fetch with it before they materialise), which covers what a grown policy would want.
- **Narrowed in S2:** the sync service fetches what a binding needs with `Fetch` before it writes the directory (a
  binding waits as `WaitingForObjects` while no peer supplies it), so a policy that grows is not what a synced folder
  depends on; the desktop keeps everything.
- **Paid in:** phase 4, with the device policies of the mobile nodes (a walk over the stored Entries of a Package when its
  policy grows).

## 30. Optional replication behaviours are not offered

- **Rule:** SPEC.md §7.6: a node SHOULD send `Have` for hashes it holds but has deferred answering; §10.2: a node that
  follows new Packages automatically (capability bit 0) MAY start following a Package it is granted.
- **Instead:** deferred answers are not announced with `Have`, and `Hello` carries no capability flag.
- **Evidence:** `Axiom.Mesh.Core/Replication/SessionOutbox.cs`, `Node/PeerSessionHandler.cs` (`OwnHello`).
- **Tolerated because:** both are SHOULD or MAY; a deferred answer is sent once the queue drains, and following is the
  user's decision in phase 1.
- **Paid in:** phase 4, with the device policies (DESIGN.md §17); the sync service of S2 needs neither: it fetches
  what a binding needs, and a binding is made by the user, who follows the Archive first.

## 32. Spawned services are configured, not spawned

Paid in S0: the daemon supervises every `services.<name>.spawn` entry whose executable is there (an absolute path, or a
bare name beside `axiom-meshd`, `SpawnPathRule`): a fresh token per start set with `LocalPort.SetSpawnedToken` before the
child starts and withdrawn when it exits; `AXIOM_MESH_SERVICE_TOKEN`, `AXIOM_MESH_PIPE`, `AXIOM_MESH_HOME` and
`AXIOM_MESH_SERVICE_STATE` in its environment, its state directory as working directory; its output in the daemon's log;
restarts by the pure `SupervisionRule`; drained, then killed after the grace, on the daemon's stop
(`Axiom.Mesh.Host/Supervision/`). An entry whose executable is absent is noted once and not supervised (S-D11). Tests:
`Host.Tests: ServiceSpawnTests` (a fake child, and the echo service spawned under `dotnet`), `SupervisionRuleTests`,
`SpawnPathRuleTests`, `SupervisorStopOrderTests`.

## 33. Package events are built from the batch, not from a diff of every field

- **Rule:** LOCAL-SPEC.md §7.3, §5.4: `PackageChanged` carries the Package after the change, the Grant for change Grant,
  the Head for change Head.
- **Instead:** a Grant change names the Grants that differ from the previous trust; a Head change names the Heads of
  the devices whose Entries the batch stored or whose Head the batch elected after an eviction (M12, entry 18); the count of stored Entries is read from the index for each record. Since M9 one builder
  (`PackageRecords.BuildAsync`) makes the record for `GetPackage`, `ListPackages`, the Package requests and the events,
  and `listedBy` counts the established sessions whose current listing names the Package.
- **Evidence:** `Axiom.Mesh.Core/Local/LocalEventHub.cs` (`EventsOf`), `Local/PackageRecords.cs`.
- **Tolerated because:** a client that needs the exact state calls `GetPackage` after an event, which §3.4 advises
  anyway; a peer listing or delisting a Package changes `listedBy` without an event of its own, which §14.12 does not ask
  for.
- **Not paid in M12:** the benchmarks of M12 put no record on a hot path (a `ListPackages` page is a handful of count
  queries), so the Entry count per Package waits for a measurement that asks for it.
- **Paid in:** phase 1b, with the Sync service's listing views, or earlier if a benchmark shows the count queries (an Entry
  count kept per Package in `PackageRow`).

## 34. ConfirmPairing is a user-key request in every mode

Paid in M12: the dispatcher asks the pairing shell for the mode of the pairing a `ConfirmPairing` names
(`PairingShell.ModeOf`, read without the lock) and `DispatchRule.SpendsUserKey` counts it as a user-key request in modes
`ShowUser`, `JoinUser` and `AddDevice`, not in `ShowDevice`; a pairing that is not the current one still counts, so
confinement errs only on the side of one request too many (`Core.Tests: DispatchRuleTests.ConfirmPairingSpendsTheUserKeyInEveryModeButShowDevice`).
LOCAL-SPEC.md §2.2 says the reference Host now does this.

## 35. An event's size is counted by encoding it once more

- **Rule:** fundamentals plan §2.5, M8 brief: events are forwarded without re-encoding.
- **Instead:** each event is made once and shared by every subscription and connection that receives it, but its frame
  size (for the 16 MiB and 64 MiB bounds) is counted by writing it into a counting buffer once, and each connection then
  encodes it again when it writes the frame. Responses are counted the same way.
- **Evidence:** `Axiom.Mesh.Core/Local/FrameSizes.cs`, `Local/CountingBufferWriter.cs`.
- **Tolerated because:** the count costs one pass over an event that is written at least once anyway, and nothing is
  allocated beyond a pooled scratch buffer; a pre-encoded event needs an API in `Axiom.Mesh.Local`.
- **Not paid in M12:** no benchmark of M12 drives events (the pipe scenarios measure requests); the counting pass stays
  until one shows it.
- **Paid in:** phase 1b, with the Sync service's event load measured (a pre-encoded `EventFrame` in Local if it matters).

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
- **Evidence:** `UnitTests/Axiom.Mesh.Host.Tests/Rules/RealPipeTests.cs`; CONFORMANCE.md (LOCAL §2.2);
  Documentation/PIPE-ACCESS.md §4.
- **Tolerated because:** the access list (owner only) and the process identity check are independent fences, each
  checked by construction and by the decision's tests.
- **Narrowed in M13:** the install job's Windows run (`install-tests.yml`, step "Another Windows account is refused by
  the pipe") creates a second local account on the runner, where the job's user is an administrator, and runs
  `axiom-mesh status` as that account with `Start-Process -Credential` against the Host the install script started; it
  must exit 2. The step is `continue-on-error` until a first run on the hosted runner shows the step itself works
  (secondary logon from the runner's session is not verifiable on the development machine, whose user is no
  administrator); it has not run anywhere yet. The development machine cannot run it.
- **Paid in:** the first green run of that step: remove its `continue-on-error`, then this entry.

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
- **Not paid in M12:** pins are not on a measured hot path; a desktop store of thousands of pins sorts within a page.
- **Paid in:** phase 1b, or earlier if a benchmark shows it (a keyset page over an ordered key, as `EntryIndex` does for
  Entries).

## 40. Paths are opened as the Host's user, not the client's

Paid in M11b by removal: no request asks the Host to open a path a client names any more. `Materialise` and `PutFile`
left the Host (LOCAL-SPEC.md draft 4, ids 317 and 515 reserved); the client library's `StoreFiles` and `Trees` read and
write files as the client's own user, and `GetStorePath` only names the store directory a service reads read-only
(`Core.Tests: StoreFilesTests.AFolderPublishedWithTheHelpersMaterialisesByteEqualFromGetObject_AndExistingIsHonoured`,
`ReadmeSamples.Tests: ContentBlindHostTests.NoPayloadTypeIsReferencedByCoreOrHost`).

## 41. An unfollowed Package that no peer lists stays Listed

Paid in M12: "no peer lists it" is read from the listings the node already keeps, the established sessions' current
listings (`NodeReplication.ListedBy`). `MeshNode.UnfollowAsync` drops the Package when no session lists it; a session whose
completed listing omits a Package it listed before drops it when the Package is only Listed here and no other session
lists it. A peer that merely disconnects omits nothing. The drop (`PackageCommandKind.Drop`, `StoreBatch.DropAsync`)
evicts the Package's Entries, Manifest and Grants and forgets its record and trust, unless something of it is still pinned
(a user or service pin), which keeps it Listed. LOCAL-SPEC.md §6.4 says so (`Core.Tests: PackageRequestTests.AnUnfollowedPackageNoPeerListsIsDropped_AndSoIsAListedOneTheLastListerOmits`).
What remains, that no event announces the drop, is entry 58.

## 42. Fetch does not check that the Package references the object

Paid in M12: `Fetch` walks the reference rows upwards from the object (`ReferenceWalk`, shared with the visibility check of
serving) and is `NotFound` at once unless the object is a stored Entry of the Package or one of its stored Entries
reaches it; nothing is wanted for a refused object (`Core.Tests: OperationKindTests.AFetchOfAnObjectNoStoredEntryOfThePackageReferencesIsNotFoundAtOnce`).

## 43. A Fetch notices a new source on its next look, once a second

Paid in M12: the want scheduler raises a source signal (`WantScheduler.WhenSourcesChange`) when a session attaches or
ends, a `Have` gains a source, a `NotFound` removes one, or a session completes a listing or receives a `PackageChanged`;
a waiting Fetch wakes on it, on a stored object or on its 60-second deadline, and polls nothing. Tests no longer move the
clock after a source connects (`Core.Tests: OperationKindTests.AFetchCompletesAsSoonAsASourceAppears_RecursivelyAndPinned`).

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
- **Not paid in M12:** retried on 2026-10-31 with the newest SDK on the development machine, 10.0.302, which still
  resolves ILCompiler 10.0.10 (the only ILCompiler in the package cache): without `--noscan` the publish fails with the same
  `CodeGenerationFailedException` for `Dictionary<long, __Canon>.CopyTo`. The argument stays; the AOT publish of
  `Scripts/package.*` (run by `install-tests.yml` and `release.yml`, which replaced `aot.yml` in M13) publishes with it,
  so the released `axiom-mesh` is compiled without the scanner.
- **Widened in S1:** the chat service (`Services/Axiom.Mesh.Chat/Axiom.Mesh.Chat.csproj`) reaches the same codec through
  `LocalClient` and fails the same way without the scanner; it passes `--noscan` too.
- **Paid in:** the first .NET 10 servicing release whose ILCompiler compiles the command line and the chat service with the
  scanner: remove both `IlcArg`s and let `install-tests.yml` prove it on both platforms.

## 48. The command line does not read `pipe.name` from `config.json`

Paid in M13: the precedence lives in one place, `PipeLocation.Resolve` (`PipeEnvironment.ConfiguredName`, used when
`AXIOM_MESH_PIPE` is unset or empty, before the default); the Host passes its `pipe.name` there, and the command line
reads `pipe.name` from the same `config.json` it reads the `cli` token from (`CliConfig.TryReadPipeName`) and resolves
through `LocalPipe.Locate(configuredName)` (`Local.Tests: PipeLocationTests.TheVariableWinsOverTheConfiguredName_WhichWinsOverTheDefault`,
`Cli.Tests: PipeNameTests`). The local install run of M13 used a home whose `config.json` names its own pipe, and the
command line found it.

## 49. A pairing code lists every interface's address, reachable or not

Paid in M11: `PairingAddressRule` lists the addresses of interfaces that are up, routable before link-local before
loopback (each only when the tier before is empty), interfaces with a gateway first, IPv4 before IPv6; a node whose
listener is bound to one address (`MeshNodeOptions.ListenAddress`, the Host's `mesh.listen`) lists that address alone
(`Core.Tests: PairingAddressRuleTests`).

## 50. The scripted peer can write a frame start beside its session

Paid in M12, in the harness: the scripted peer's AMP/1 and pairing sessions write through a `SerialWriteStream` over its
TLS stream, and `SendFrameStartAsync` and `SendRawAsync` write and flush through the same stream under the same lock, so
the TLS stream never sees two writes at once, heartbeats and automatic answers included. ProtoStream was not changed: a
lock at the stream is enough for the harness, and the product writes only through `Session.WriteAsync`.

## 51. The socket fixture keeps its nodes' keys in memory

- **Rule:** ARCHITECTURE.md rule 3 (keys only in the Host's key store) as the shipped daemon applies it.
- **Instead:** `RealMeshFixture` gives each real node an `InMemoryKeyStore` that outlives its restarts; the Host's
  `FileKeyStore` is internal to the Host and covered by `Host.Tests` (`FileKeyStoreTests`, `RealPipeTests`).
- **Evidence:** `IntegrationTests/Axiom.Mesh.Integration.Tests/Support/RealNode.cs`.
- **Tolerated because:** the scenarios test replication, trust and the pipe, not key storage.
- **Narrowed in M13, by decision:** the published daemon's `FileKeyStore` is run end to end where it ships: the AOT smoke
  (`Scripts/aot-smoke.*`, inside `Scripts/package.*`) and the install job (`install-tests.yml`) start the Native AOT
  `axiom-meshd`, create an identity (both keys written, DPAPI-wrapped on Windows, 0600 under a 0700 directory on Linux),
  and write and read an Entry signed with the key read back from the files. The socket scenarios keep their keys in
  memory on purpose: they restart nodes many times per run, and their subject is replication over real sockets, not key
  storage, which `Host.Tests: FileKeyStoreTests` covers by rule. Giving `RealNode` the Host's internal key store would
  open the Host's internals to the integration suite for no new proof.
- **Paid in:** nothing further planned; the entry stays as the record of the decision until the release after 0.1.0
  re-reads it.

## 52. A history walk asks for one parent per round trip

Paid in M12. A linear history names each parent only in its child, so a walk is one round trip per Entry by nature; what
made it slow was everything put on that round trip. Two changes took it off: the parents of an Entry are wanted from its
sender as soon as intake admitted it, not once its batch committed (`Replicator.WantParentsAsync`), and the store writer
writes a batch's object files several at a time before the transaction (`FileWrites`), where one flushed file per Entry
had bounded the writer to about one Entry per millisecond and filled its queue. 10 000 Entries now walk in 9.25 s on
loopback with real TLS (Documentation/BENCHMARKS.md, `scenario walk`; M11 measured 42.9 to 51.5 s), within the 10 s target;
the release floor run the same evening measured 11.7 s, so on this laptop the walk sits at the target, within its
run-to-run spread. What is left per Entry is a round trip with a signature check and a file open on the serving side.
The release floor `FloorTests.AHistoryOfTenThousandEntriesIsWalkedWithinTwoMinutes` stays as the guard.

## 53. A cancelled read can leave the next write encrypted and refused

- **Rule:** ARCHITECTURE.md rule 6 (every wait a deadline) without corrupting the stream it bounds.
- **Instead:** ProtoStream 0.2.0 flushes a session's automatic answers (a `Pong`) under the token of the read that
  received the `Ping`; a read cancelled just as that flush ends leaves `StreamPipeWriter`'s internal cancellation set, and
  the next write hands an already-cancelled token to `SslStream`, which encrypts the record and then has it refused by
  the transport: the record sequence has a gap and the peer reports "decryption failed". M11 found this as the flaky
  `ListingTests("61 seconds")` (about one run in twelve of a 400-iteration stress, none in 1 600 after the fix).
- **Paid in Core (M11, M12):** no session read of a mesh connection is cancelled any more. M11 abandoned the read for the
  violations a session raises outside it; M12 does the same for the drain path and a closing session (`ReplicationSession`
  races the read against the stopping token and abandons it) and for the frame bound and the drain before `Hello`
  (`PeerSessionHandler` reads through `AbandonableRead`). Writes are never cancelled once begun.
- **Still open, upstream:** the hazard itself is ProtoStream's: any other user of a session (the local port's
  connections, the pairing shell) that cancels a read can still meet it.
- **Evidence:** `Axiom.Mesh.Core/Node/AbandonableRead.cs`; ProtoStream `Session.cs` (automatic reply under the read's
  token), `StreamPipeWriter` (cancellation kept for the next flush).
- **Paid in:** a ProtoStream release that flushes automatic answers without the read's token (or resets the writer's
  cancellation before a write); then `AbandonableRead` can go.

## 54. The latest-only retention row knows the `Version` kind

- **Rule:** DESIGN.md §11 and plan §6f: the Host parses no payload and leaves the meaning of a kind to its service.
- **Instead:** the phone row of SPEC.md §11.2 keeps, for an Archive, only what the newest `Version` references; the
  store writer recognises that Entry by its envelope `kind` 1 (`Axiom.Mesh.Core/Store/StoreBatch.cs`
  `RecordNewestVersionAsync`, `Store/PinBook.cs` `ApplyPolicyAsync`). No payload is read, but one registered kind has a
  meaning inside the node.
- **Evidence:** `EntryKind.Version` in those two files; SPEC.md §11.2 (draft 5) names the kind.
- **Tolerated because:** the rule is the specification's, and the kind is read from the signed envelope, which is
  routing data; a node that parses nothing still keeps the row.
- **Decision S-D4 (2026-11-01):** the per-kind retention declaration stays deferred: nothing on a desktop applies the
  latest-only row, and a `DeclareRetention` request would add a persisted registry and a race between a service's
  declaration and the Entries stored before it for no gain in phases 1b and 2. The node keeps reading envelope kind 1
  for the row until then.
- **Paid in:** phase 4 (phones), where the row applies: the service declares what its kinds keep under each row (a
  field of `RegisterKind`, or of the Manifest), and the node applies that declaration instead of knowing kind 1.

## 55. The file helpers touch the file system directly

- **Rule:** ARCHITECTURE.md rule 8: time, sockets, disk and the pipe are injected edges.
- **Instead:** the deciding parts of the helpers are pure (`Axiom.Mesh.Local/Files/FileNameRule.cs`,
  `MaterialisePlan.cs`, `ExistingRule.cs`, `FolderTrees.cs`, `ObjectReader.MayReadDirectly`) and the client is handed in,
  but the shells (`StoreFiles.cs`, `Trees.cs`, `TreeWriter.cs`, `ObjectReader.cs`) call `System.IO` themselves; tests use
  real temporary directories.
- **Evidence:** those files; `Core.Tests: StoreFilesTests` writes to the system's temporary folder.
- **Tolerated because:** the shells only sequence what the pure parts decided, and a file-system seam in the public
  surface of the package every app references is a design decision of its own.
- **Narrowed in M13, by decision:** the real file system is the edge of these helpers. Their job is file I/O as the
  client's own user (LOCAL-SPEC.md draft 4 moved it out of the Host for exactly that), every decision in them is a pure
  function tested by rule (`FileNameRule`, `MaterialisePlan`, `ExistingRule`, `FolderTrees`, `ObjectReader.MayReadDirectly`),
  and what a fake file system would replace (temporary names, renames, the marker file, symlinks becoming files, a
  cancelled write leaving nothing visible) is what the real-directory tests are there to prove; an in-memory fake would
  prove the fake. An internal seam was weighed and not built: it would add an interface every shell call goes through and
  no test that fails today.
- **Narrowed in S2:** the pure parts the sync service needs are public (`FileNameRule` with `ReservedPrefix`,
  `FolderTrees.TryBuild` over `FolderFile`s, `MaterialisePlan`, `PlannedFile`, `PlannedFileKind`, `ObjectReader`) and one
  file is written with `StoreFiles.WriteFileAsync`; the sync service's own scanning and watching are seams of the service
  (`IDirectoryScanner`, `IFolderWatcherFactory`), faked in its tests, not of the helpers. A local directory is all it
  needs.
- **Paid in:** phase 4, only if a node needs the helpers over something that is not a local directory (a phone's
  content provider, for one); then the seam is designed for that, public, with its first real second implementation.

## 56. A store written before SPEC.md draft 5 is not refused at start

Paid in M13: `MeshDatabase.SchemaVersion` is 2 (the Entry layout of draft 5, the `UserRow.Name` column of M11 and the
`Parents` table of M12), and a store of version 1 is refused at start with a `MeshStoreException` (the daemon exits 4)
whose steps say there is no migration before 0.1.0: stop the Host, delete the pre-release data and keys directories,
start again, create the identity, pair again (`Core.Tests: StoreSequenceTests.AStoreOfAPreReleaseBuildIsRefusedAtStart_AndTheStepsSayToDeleteIt`).
A store of a newer version keeps the older refusal ("run the version that made it"). CHANGELOG.md says both, for 0.1.0.

## 57. Publishing a file costs one pipe round trip per Chunk

Paid in M12: `StoreFiles.PutFileAsync` keeps up to `DefaultChunksInFlight` (8) puts outstanding, or a window of 1 to 64
the caller chooses (an overload), each Chunk read straight into a reused buffer behind its header and sealed in place
(`Chunker.Seal`, no allocation per Chunk, `ChunkWindow`); the ChunkList names the Chunks in file order whatever order the
Host answers in, the first refusal in file order ends the file, and memory stays at the window's buffers. The Host runs
`PutObject` outside the connection's ordered lane (it writes no Package, LOCAL-SPEC.md §3.4) and queues the object and its
pin back to back, so they share a batch. A 256 MiB file: 84 MiB/s with one in flight, 136 MiB/s with eight; the Host before
M12 with one in flight: 80 MiB/s (Documentation/BENCHMARKS.md, `scenario putfile`). The writer's per-object flush bounds it
from there (entry 60). `Core.Tests: StoreFilesTests.APipelinedPutStoresTheSameFileInFileOrderAsOneAtATime`.

## 58. A dropped Package is announced by no event

Paid in M13, before 0.1.0: LOCAL-SPEC.md §14.12 registers change kind 8, `Dropped` (`PackageChange.Dropped`), and §6.4
says a drop is announced. The store writer's drop publishes it (`StoreBatch.DropAsync`), the event hub builds the record
from the trust before the drop (the Package's last name, kind and owner, `state` Listed, no Heads, `entries` 0), and an
`UnfollowPackage` that drops reports `Unfollowed` then `Dropped`
(`Core.Tests: PackageRequestTests.AnUnfollowedPackageNoPeerListsIsDropped_AndSoIsAListedOneTheLastListerOmits`; the
registry drift test reads the new row). Decided before the release because the registry is closed once clients exist,
and a client otherwise had to poll `ListPackages` to notice a drop.

## 59. A faulty Entry found after it was stored is not recorded against its sender

- **Rule:** SPEC.md §12.2: the sender of faulty content is recorded as faulty for that object.
- **Instead:** an Entry found faulty when its last parent arrives (entry 28) is retired from the store, but the index does
  not keep which peer sent it, so no peer is marked faulty for it; an Entry found faulty on arrival still is
  (`FaultyContentTests`).
- **Evidence:** `Axiom.Mesh.Core/Store/StoreBatch.cs` (`RetireAsync`); `EntryRow` has no origin column.
- **Tolerated because:** "recorded" serves the peer table's diagnostics and backoff, not trust: the Entry is gone either
  way, and a peer that serves it again sees it refused as faulty on arrival and recorded then.
- **Paid in:** phase 1b, with the peer table persisted (entry 23): an origin column on the parent rows of Entries stored
  from a peer.

## 60. Replication moves about 190 MiB/s over loopback, half the target

- **Rule:** fundamentals plan §5: 1 GiB of Chunks between two nodes over loopback with real TLS at 400 MiB/s or more.
- **Instead:** 5.46 s, 188 MiB/s in the recorded run (Documentation/BENCHMARKS.md, `scenario loopback`; 249 MiB/s in an
  earlier run the same day). The receiving store writer bounds it: every object is its own file, written to a temporary
  name, flushed and renamed (SPEC.md §11.1), now several at a time (`FileWrites`, 8 at once); and every object stored,
  served and checked costs index reads and writes (the visibility walk on the sender, the object, reference and pin rows on
  the receiver).
- **Evidence:** `Axiom.Mesh.Core/Store/FileWrites.cs`, `Store/StoreBatch.cs`; `StoreBenchmarks.Put1MiB` (3.2 ms per
  object in a batch of its own).
- **Tolerated because:** the release floor (60 s for 1 GiB) holds by a factor of ten, and a desktop catching up on a
  folder is bound by its disk as much as by this path.
- **Paid in:** phase 1b, with a measured choice among: more files written at once, one flush per batch on platforms where
  a directory flush covers its renames, and fewer index rows per Chunk (a Chunk's object row written with its ChunkList's).

## 61. Signing an Entry verifies it again, halving the signing rate

- **Rule:** fundamentals plan §5: at least 10 000 Entries signed per second per core.
- **Instead:** `EntryDraft.Sign` encodes, signs, parses the result back and verifies it against the signer's own key
  (M1, so the encoder is proven on every signature); with ECDSA P-256 on .NET that is about 158 µs, 6 300 per second
  per core (Documentation/BENCHMARKS.md, `EntrySignatureBenchmarks.SignEntry`); the verification alone is 78 µs.
- **Evidence:** `Axiom.Mesh.Model/Producers/EntryDraft.cs`.
- **Tolerated because:** a device writes Entries at human pace, and the store writer's commit (about 3.5 ms per Entry,
  `IndexBenchmarks.EntryThroughTheWriter`) bounds a burst far below the signing rate; the self-check catches an encoder
  defect before anything is stored or sent.
- **Paid in:** phase 1b if a service writes faster than 6 000 Entries a second: verify the own signature in Debug builds
  and in tests only, or check the re-parsed bytes without the signature.

## 62. A thousand Entries in one LDB transaction take about 90 ms

- **Rule:** fundamentals plan §5: 1 000 Entries committed in one transaction within 50 ms.
- **Instead:** 88.4 ms ± 25.4 ms for 1 000 Entry rows and their object rows in one write transaction at full durability,
  with 49.9 MB allocated inside LDB (Documentation/BENCHMARKS.md, `IndexBenchmarks.ThousandEntryRowsInOneTransaction`):
  LDB 0.1.0 maintains five indexes per Entry row (hash, store sequence, Package, clock, redaction target) and two per
  object row, and allocates its query and index arrays per row.
- **Evidence:** `Axiom.Mesh.Core/Storage/EntryRow.cs` (the indexes); the benchmark above. One Entry per commit meets its
  target (1 100 commits/s, `EntryRowCommit`).
- **Tolerated because:** the store writer batches at most 256 commands, so a batch of Entries costs about 23 ms; a history
  walk of 10 000 Entries meets its own target (debt 52).
- **Paid in:** the LDB release that pools its per-row buffers; or, measured first, fewer Entry indexes (the redaction
  target as a separate table holding only redactions).

## 63. A `GetIdentity` allocates more than two objects in the Host

- **Rule:** fundamentals plan §5: `GetIdentity` over the real pipe with at most 2 allocations in the Host.
- **Instead:** the whole process (client, pipe and Host) allocates 3 568 bytes per round trip (Documentation/BENCHMARKS.md,
  `scenario identity`); the client's share is at most 320 bytes (`Local.Tests: AllocationTests.AGetIdentityRoundTripOnTheClientAllocatesOnlyTheMessages`),
  so the pipe and the Host account for about 3.2 KB: the request's state machines in the read loop and the handler, the
  `Identity` record and its strings, the answer and its outbox entry, the frame's lazy body.
- **Evidence:** `Axiom.Mesh.Core/Local/LocalConnection.cs` (`Accept`, `RunAsync`, `Answer`), `Local/IdentityRequests.cs`.
- **Tolerated because:** the latency targets hold with room (p50 66 µs, p99 115 µs), and nothing grows with the number of
  requests; the budget was set before the local port was built.
- **Paid in:** phase 1b, with a Host-only measurement (a client in another process) and an allocation budget as a
  `[ReleaseFact]` for the request path; a cached `Identity` answer and pooled answers are the first candidates.

## 64. No Native AOT service is spawned by a test yet

- **Rule:** DESIGN.md §10.3 (S-D1): the first-party services ship as Native AOT executables the Host spawns.
- **Instead:** the Host's supervision is proved with a fake child and with the echo service spawned as a
  framework-dependent program under `dotnet` (`Host.Tests: ServiceSpawnTests`); no AOT-published service exists yet,
  so neither `Scripts/aot-smoke.*` nor `install-tests.yml` starts one, and the Linux paths of the supervisor
  (`Process.Kill(entireProcessTree)` on Linux, the 0700 state directory, a socket path in `AXIOM_MESH_PIPE`) have run on
  no machine.
- **Evidence:** `UnitTests/Support/Axiom.Mesh.EchoService`; `Axiom.Mesh.Host/Supervision/SystemChildProcesses.cs`.
- **Tolerated because:** the supervisor treats every child alike; what an AOT child adds is its publish, which is the
  service's own build.
- **Narrowed in S1:** `Scripts/aot-smoke.ps1` publishes `axiom-mesh-chat` with Native AOT beside the daemon, the daemon
  spawns it and `chat send`/`chat show` go through it (passed on win-x64 on 2026-11-01); `Integration.Tests:
  ChatProcessTests` spawns the framework-dependent apphost from a real daemon process, kills it and sees it restarted.
  Left: `aot-smoke.sh` and both `install-tests.yml` jobs (which now assert `chat` answering) have run nowhere, so the
  supervisor's Linux paths are still unproved.
- **Paid in:** the first green `install-tests.yml` run on both platforms (the user's push before `v0.2.0`).

## 65. No topic tells a client that a service connected or left

- **Rule:** DESIGN.md §10.5: the app shows each service's state live, from the local protocol, without polling.
- **Instead:** LOCAL-SPEC.md draft 5 §7.2 states that there is no presence topic: a client learns whether a service is
  connected from `ListServices`, and a `ServiceCall` to one that is not fails at once with `ServiceUnavailable`; the
  service's own events stop while it is gone.
- **Evidence:** LOCAL-SPEC.md §7.2 note; `Axiom.Mesh.Cli/Commands/ServicesCommand.cs` reads `ListServices`.
- **Tolerated because:** the CLI asks once per command, and `ServiceUnavailable` is immediate; no client keeps a live
  view of services before the app.
- **Paid in:** phase 3 (the app): a `services` topic (an extension topic id, LOCAL-SPEC.md §1.5) or a change kind on an
  existing one, decided with the app's first service view.

## 66. The chat index keeps a second copy of every message's text

- **Rule:** DESIGN.md §13 (S-D2): a service's index is a cache rebuilt from the Host; the bytes of an Entry are an object
  in the Host's store, held once.
- **Instead:** `ChatMessages.Text` holds each message's text (up to 16 KiB) and its attachments as parsed, beside the Entry
  object the Host stores, so a timeline page is read from the index alone.
- **Evidence:** `Services/Axiom.Mesh.Chat/Index/MessageRow.cs`; CHAT-SERVICE.md §10.
- **Tolerated because:** reading 256 Entry objects through the pipe or the store directory per page would cost a file per
  message; the copy is a cache, dropped with its Channel and rebuilt by store sequence.
- **Paid in:** phase 3, when the app's timeline shows what the chat service needs to keep: read texts from the store
  directory (LOCAL-SPEC.md §9.7) if a page measured that way stays under the 5 ms target.

## 67. The chat index's attachment key is concatenated

- **Rule:** as entry 15: a two-part key is a composite key.
- **Instead:** `ChatAttachments.Key` holds the attachment's hash followed by the message's, beside both as indexed columns
  of their own; LDB 0.1.0 as published allows one `[Key]` property.
- **Evidence:** `Services/Axiom.Mesh.Chat/Index/AttachmentRow.cs`, `RowBytes.Pair`.
- **Tolerated because:** the parts are fixed-length hashes, so the concatenation is unambiguous; entry 15 pays both.
- **Paid in:** with entry 15, when an LDB release with `[CompositeKey]` is on nuget.org.

## 68. An attachment no message named stays pinned until the chat service starts again

- **Rule:** LOCAL-SPEC.md §9.5, §9.6: a service releases the pins of what it put once it no longer needs them.
- **Instead:** the objects an `attach` job put keep the chat service's pins (reason `Service`) until a `send` names the
  attachment; one never sent is released only at the service's next start (`ChatPending`), or by the user's
  `ReleasePins`. A store with a small budget can fill with attachments nobody sent while the service runs.
- **Evidence:** `Services/Axiom.Mesh.Chat/ChatService.cs` (`TakeAllPendingAsync` at the first connect),
  `Shell/ChatJobs.cs`; `Chat.Tests: ChatServiceTests.ThePinsOfAnAttachmentNoMessageNamedAreReleasedAtTheNextStart_AndTheIndexIsKept`.
- **Tolerated because:** an attachment is put for a message about to be sent; the CLI sends right after its jobs end.
- **Paid in:** phase 3 (the app): an expiry for pending attachments (a `ChatPending` row older than a day is released),
  decided with the app's compose view, which may hold attachments longer.

## 69. A file rewritten with its size and write time unchanged is not seen

- **Rule:** SYNC-SERVICE.md §3 step 3: a file whose size, write time and mode match its fingerprint is not read.
- **Instead:** the racy rule (a fingerprint taken less than 2 s after the file's last write is read again, as Git does)
  closes the same-size edit within one tick of the file system's clock; a file rewritten later with the same size and a
  write time set back to the old one (a tool that restores timestamps) is not seen until its size or time changes.
- **Evidence:** `Services/Axiom.Mesh.Sync/Rules/ChangeDetection.cs` (`RacyWindow`);
  `Sync.Tests: ChangeDetectionTests.AFingerprintTakenRightAfterTheWriteIsRacy_TheFileIsReadAgain`.
- **Tolerated because:** editors and copy tools set a new write time; reading every file at every run would cost a full
  read of the folder each time.
- **Paid in:** phase 3, with the app's "check every file now" (a rescan that reads every file and compares hashes).

## 70. Local symlinks are not published; a branch's symlinks are written as files

- **Rule:** SPEC.md §8.4 lets a Tree hold symlinks (mode 3); SYNC-SERVICE.md §2 keeps them out of draft 1.
- **Instead:** a symlink in a bound directory is skipped by the scan and never followed nor published; a symlink of the
  branch is written as a regular file holding its target text (or not at all with `symlinks = Ignore`), and its fingerprint
  remembers it so that a later scan publishes it unchanged.
- **Evidence:** `Services/Axiom.Mesh.Sync/Shell/DirectoryScanner.cs`, `Rules/ModeMemory.cs`, `Rules/LocalTree.cs`.
- **Tolerated because:** a link points outside the folder as often as inside, Windows creates links only with a privilege,
  and writing a link a peer chose is a way out of the bound directory; a file holding the target loses nothing.
- **Paid in:** a later phase (DESIGN.md §17, "later"), with a decision on links that stay inside the folder.

## 71. Paths longer than the platform's classic limit are not tested

- **Rule:** SYNC-SERVICE.md §9: a path of up to 4 096 bytes.
- **Instead:** the service builds paths with `Path.Combine` and relies on .NET's long-path support on Windows (the
  manifest of a .NET application opts in); no test binds a folder whose paths pass 260 characters.
- **Evidence:** `Services/Axiom.Mesh.Sync/Shell/DirectoryScanner.cs`, `Shell/Materialiser.cs`; no such scenario.
- **Tolerated because:** .NET 8 and later read and write long paths on Windows 10 and later when the system allows them;
  a path the file system refuses is a `NameRefused` or `Unreadable` conflict, never a crash.
- **Paid in:** phase 3, with the app's Windows installer, which also sets `LongPathsEnabled`.

## 72. A directory of more than 8 192 entries is refused, not split

- **Rule:** SPEC.md §8.4 bounds a Tree to 8 192 entries.
- **Instead:** such a directory is a `TooManyEntries` conflict; it keeps the branch's state and nothing of it is published.
- **Evidence:** `Services/Axiom.Mesh.Sync/Shell/DirectoryScanner.cs`; SYNC-SERVICE.md §3 step 2.
- **Tolerated because:** the bound is the specification's; splitting a directory into several Trees needs a layout SPEC.md
  does not define.
- **Paid in:** not planned; with a SPEC.md draft that defines a split directory, if users meet the bound.

## 73. A moved file is a deletion and an addition

- **Rule:** DESIGN.md §17 (sync "keeps a folder in step"); no rule demands rename detection.
- **Instead:** a renamed or moved file publishes its new path and deletes its old one; a conflict between a rename on one
  side and an edit on the other keeps the edited file at the old path and the renamed file at the new one.
- **Evidence:** `Services/Axiom.Mesh.Sync/Rules/Merge.cs` (path by path).
- **Tolerated because:** the bytes are content-addressed, so a move sends no Chunk again; nothing is lost.
- **Paid in:** a later phase (DESIGN.md §17, "later").

## 74. Every merge of a chain is written as a Version

- **Rule:** SPEC.md §9.5: concurrent leaves are merged two at a time until one remains.
- **Instead:** each two-parent merge of a chain is written as its own Version, also when only the last one is ever shown;
  three leaves make two merge Versions.
- **Evidence:** `Services/Axiom.Mesh.Sync/Engine/SyncRun.cs` (`ReduceAsync`);
  `Sync.Tests: SyncScenarios.ThreeConcurrentLeavesResolveByAChainOfTwoParentMerges_IdenticallyEverywhere`.
- **Tolerated because:** a merge Version holds only a new root and the Trees that changed; writing every step keeps each
  merge reproducible from its two parents, which is what lets another device arrive at the same Tree.
- **Paid in:** not planned.

## 75. A conflict is recorded only by the device that merged

- **Rule:** SYNC-SERVICE.md §8: a conflict's copy lies beside the winner on every device.
- **Instead:** the copy is in the merged Tree, so every device writes it, but the conflict row (and the `conflict` event)
  exists only on the device that made the merge; the other device sees the copy in its folder and no row.
- **Evidence:** `Services/Axiom.Mesh.Sync/Engine/SyncRun.cs` (conflicts recorded at the end of a merging run).
- **Tolerated because:** the files are identical on both; a conflict list derived from a pulled Version would need the
  merge to be recomputed on pull.
- **Paid in:** phase 3, with the app's conflict view (a pulled merge Version is compared with its parents to list its
  copies).

## 76. The scan reads file times against the system clock

- **Rule:** ARCHITECTURE.md rule 8: time is an injected edge.
- **Instead:** whether a file is busy (changed within 2 s) and whether a fingerprint is racy are decided against
  `DateTimeOffset.UtcNow`, because the file system stamps write times with the system clock; the debounce, the rescan and
  the events run on `SyncServiceOptions.Time`.
- **Evidence:** `Services/Axiom.Mesh.Sync/Engine/SyncRun.cs` (`ScanAsync`); `Rules/ChangeDetection.cs` takes the time as an
  argument and is tested by rule.
- **Tolerated because:** comparing a file system's times with a fake clock would compare two clocks; the rule itself is
  pure, and tests set a stability of zero.
- **Paid in:** not planned.

## 77. Ten thousand small files take about 80 seconds between two nodes on one machine

- **Rule:** plan §9.4 item 6: scenario 6 (10 000 small files and 64 MiB) across two real nodes within 60 s.
- **Instead:** 78 to 84 s on the development machine (Windows 11, both Hosts and both services on one disk); the floor test
  holds 120 s. Measured per small file: two `PutObject`s at about 1.2 ms each on the publishing Host (it stays near that
  from 16 to 60 requests in flight), about 1.4 ms per object replicated and stored on the other Host, and about 1.7 ms to
  write it there, most of it the flush to disk before the rename (SPEC.md §11.4: nothing partial under its name).
- **Evidence:** `Integration.Tests: SyncFloorTests.TenThousandFilesAndSixtyFourMebibytesArriveWithinTwoMinutes`;
  BENCHMARKS.md, sync. A probe found and S2 fixed what was the service's own: its per-file tables were keyed with the
  binding's id first, and LDB keys a byte column by its first 8 bytes, so every insert checked every row of the binding
  (254 s before, `Sync.Tests: StoreKeyTests`).
- **Tolerated because:** a folder is synced once and then file by file; the unchanged scan (0.59 s for 100 000 files) and
  a change (0.07 s after the debounce) are well within their floors.
- **Paid in:** phase 3, with the Host's small-object path (several objects in one `PutObject` batch and one flush, the
  replicator's per-object store cost); the service's share is already concurrent.

## 78. A user connection polling a busy service was seen wedged in its write

- **Rule:** LOCAL-SPEC.md §3.4: the Host reads every connection; a request is answered or refused.
- **Instead:** once, in a full run of the solution (every suite at once), the socket scenario of 1 000 files had its test's
  one user connection, which asked the sync service for its status in a tight loop while Bob's Host ran a large `Fetch`,
  stop completing writes: a status call returned its reply, but the flush of that request's frame never completed, so the
  connection's write lock stayed held and the next call waited for an hour. A dump showed nothing running: the Host's read
  loops waited for input, the client's flush for the pipe. Not root-caused, and not seen again once the loop paused between
  calls; the later failures of that scenario were a `Fetch` that missed arrivals after its change subscription lapsed
  (fixed in S2, `Axiom.Mesh.Core/Local/ObjectFetcher.cs`).
- **Evidence:** `Sync.Tests: SyncScenarios` (`SettledAsync` now opens a connection per look and bounds each call); the
  transport is ProtoStream's `Session` over the named pipe (`AwaitFlushAsync`).
- **Tolerated because:** it was seen once, under a load no user produces, and a fresh connection per look sidesteps it.
- **Paid in:** phase 3, with the app (which holds one connection for its lifetime): a stress test of one connection
  polling a service during a large `Fetch` under load.

## 79. Windows Defender flags the unsigned command line as persistence malware after `host install`

- **Rule:** DESIGN.md §17 and ACCEPTANCE.md step 1: `install.ps1` installs the zip, registers the Host to run at logon and
  prints `axiom-mesh status`; nothing needs administrator rights.
- **Instead:** on the first acceptance run (2026-11-02, Windows 11 Enterprise 26200, Defender with cloud protection) the
  install succeeded and printed the status block, then Defender's behaviour model raised `Behavior:Win32/Persistence.A!ml`
  (severity 5) against `axiom-mesh.exe`: an unsigned executable had just created a scheduled task (`host install`). From
  that moment every copy of that file, by hash, is refused at start ("the file contains a virus or potentially unwanted
  software"), including the untouched publish folder. The Host and both services kept running; only the command line was
  blocked. The user must allow it in Windows Security (Protection history → Allow) or exclude the install directory.
- **Evidence:** `Get-MpThreatDetection` on the laptop; the second PC of the acceptance run.
- **Tolerated because:** the binaries are unsigned (a code-signing certificate is a purchase), and the detection is a
  reputation model over an unknown file performing persistence, not a finding in the code.
- **Paid in:** before 0.3.0, one of: sign the executables (Authenticode, which also quiets SmartScreen); let `install.ps1`
  register the scheduled task itself so the persistence actor is PowerShell, not the unknown executable, with
  `host install` kept for hand installs; submit the file to Microsoft as a false positive per release. Until then
  ACCEPTANCE.md step 1 names the allow step.

## 80. Discovery says neither that a pairing is open nor the device's name

- **Rule:** PAIR-SPEC.md §2.4: the entering side names the showing device from what discovery announced; ACCEPTANCE.md
  step 3: the user on B picks A from a list without typing anything of A's.
- **Instead:** an announcement (SPEC.md §3.1, §3.2) carries two ids and a port, nothing else: no device name and no sign
  that a pairing waits. `pair join` therefore lists every discovered device that is in no Roster the Host holds, by id,
  and the user tells A's apart by the id prefix `pair show` prints on A. A name or a "pairing open" field is a wire
  change of both codecs: the broadcast datagram refuses unknown flag bits, and a DNS-SD TXT key beside `v`, `d` and `u`
  is a new version of the record; neither can be added without a version a v1 reader ignores.
- **Evidence:** `Discovery.Tests` (the codecs refuse what they do not know);
  `Cli.Tests: CliScenarioTests.PairJoinWithoutACodeListsTheDiscoveredDevices_AndJoinsOneByItsIdPrefix` picks by id.
- **Tolerated because:** on a home or office LAN the list holds one or two devices, and the prefix is on the other
  screen; a wrong pick cannot pair without the comparison (PAIR-SPEC.md §10).
- **Paid in:** the next discovery version, with the name the app shows and a pairing flag (SPEC.md §3), when the app
  needs a list of names.

## 81. A service keeps the identity its first `HelloAck` carried

Paid in S3 (before v0.2.0): the runtime's `IdentityWatch` subscribes to topic `users` on every connection, reads
`GetIdentity` after the subscription's response and again on every `UserChanged` of the own user (and after a lapse), and
`ServiceSession.Identity` is that live value, `null` while the Host has none; `ServiceSession.Host` stays the `HelloAck`
for the store epoch and sequence (LOCAL-SPEC.md §9.1 now says so). Chat's `withdraw` and Sync's runs decide by it; a sync
run without an identity ends `Error` before it reads a byte. `Service.Tests: IdentityTests`, `HelloAckTests`,
`Chat.Tests: ChatServiceTests.AServiceRunningBeforeIdentityCreate…`, `Sync.Tests: SyncServiceTests.AServiceRunningBeforeIdentityCreate…`.

## 82. A Host started by the logon task gets no firewall prompt

- **Rule:** ACCEPTANCE.md *Before you start*: inbound TCP 41810 open; DESIGN.md §17: two PCs pair and sync out of the box.
- **Instead:** `host install` starts `axiom-meshd` headless (`conhost --headless`) from a scheduled task; Windows shows the
  firewall prompt only to a program with a window, so on a Public-profile network inbound 41810 stays blocked and the
  other PC cannot dial in. The mesh survived it (the reachable side is dialled), pairing by code had to be joined from the
  reachable PC, the offline catch-up took 71 s instead of seconds because the dialler's backoff had doubled while the
  peer was down, and Eve had to be dialled from A.
- **Evidence:** `Documentation/acceptance-runs/2026-11-02.md` item 2.
- **Tolerated because:** a firewall rule needs an administrator and `install.ps1` is deliberately non-admin.
- **Paid in:** before 0.3.0: `host install` adds the rule when it runs elevated and prints the `New-NetFirewallRule` line
  otherwise; `axiom-mesh status` reports whether the mesh port is reachable from the LAN; a fresh discovery announcement
  from a peer resets its dial backoff.

## 83. Two PCs that reconnect at once both write the same merge Version

- **Rule:** SPEC.md §9.5 and SYNC-SERVICE.md §4: the same leaves merge to the same Tree everywhere; a merge equal to the
  later leaf's Tree writes no Version.
- **Instead:** both sides merged within the same second and each wrote a Version with the merge Tree (86fd614b7a1f and
  bbd5ad6fcf43, tree 2b9844e2fc1e); correct, but `sync status` shows `in sync`, `Leaves 2` and the conflict on both until
  the next change names both as parents.
- **Evidence:** `Documentation/acceptance-runs/2026-11-02.md` item 5; `Sync.Tests` scenario 4 covers one merging side.
- **Tolerated because:** nothing is lost and the next publish resolves it; the status is what confuses.
- **Paid in:** phase 3: either the side with the lower device id waits one debounce for the other's merge before writing
  its own, or `sync status` says "two equal leaves" instead of counting them.

## 84. Conflict rows never close, and the two directory rules disagree

- **Rule:** SYNC-SERVICE.md §6: a conflict is listed until the user removes one copy; ACCEPTANCE.md step 6 and 12.
- **Instead:** the row and `Conflicts 1` stay after the copy was deleted on either PC; `package materialise` refuses an
  existing empty folder, `sync add` refuses a missing one.
- **Evidence:** `Documentation/acceptance-runs/2026-11-02.md` items 5 and 15.
- **Tolerated because:** cosmetic; the folders were right.
- **Paid in:** phase 3: a conflict row closes when its copy is gone from the Tree; both commands accept an empty folder
  whether it exists or not.

## 85. A pairing join into a blocked port holds the Host busy for minutes

- **Rule:** PAIR-SPEC.md §6.3: a pairing that is not final at its expiry fails; LOCAL-SPEC.md §6.2 `StartPairing` is
  `Busy` while one runs.
- **Instead:** the entering side dials every address of the code in turn and each connect into a firewall waits for the
  TCP timeout (about 20 s on Windows), six addresses in the run; `pair show` on that PC was `Busy` meanwhile and
  `pair cancel` printed "Pairing 2 is not the current one, or has ended" for the join that had just ended.
- **Evidence:** `Documentation/acceptance-runs/2026-11-02.md` item 3.
- **Tolerated because:** pairing by discovery (debt 80, commit e180e0a) dials one known address.
- **Paid in:** phase 3: a connect deadline of a few seconds per address, the first reachable address wins, and `pair
  cancel`/`pair show` explain a join still dialling.

## 86. The TLS scan on CI sees a session ticket

Paid in S3: .NET's `AllowTlsResume = false` sets only `SSL_OP_NO_TICKET` on OpenSSL (`CryptoNative_SslCtxSetCaching`,
dotnet/runtime `pal_ssl.c`, release/8.0 and release/10.0), which in TLS 1.3 means a stateful ticket, not none
(OpenSSL `SSL_CTX_set_options(3)`); no option of `SslServerAuthenticationOptions`, `AppContext` switch or environment
variable reaches `SSL_set_num_tickets`. `OpenSslTickets.IssueNone` calls it with zero on the handle `SslStream` keeps in
its private `_securityContext`, from a `FirstWriteHook` under the server stream, while the first flight is written.
Reproduced and proved in a container (Ubuntu 24.04, OpenSSL 3.0.13, net8.0 and net10.0): `Integration.Tests:
TlsConformanceTests.OnLinuxTheMeshPortIssuesNoSessionTicket` and `OnLinuxTheRecordCountSeesTheTwoTicketsABareSslStreamServerStillIssues`;
testssl.sh 3.2 and `check.py` as `tls-scan.yml` runs them pass there ("Session Ticket RFC 5077 hint: no"). What remains
is entry 87.

## 87. No session ticket on Linux rests on a private field of `SslStream`

- **Rule:** SPEC.md §4.2: session tickets MUST NOT be issued, and a rule of the spec is kept through what the runtime
  offers publicly, not through its private fields.
- **Instead:** `OpenSslTickets` reads `SslStream`'s private `_securityContext` by reflection and calls
  `SSL_set_num_tickets` in the libssl the runtime mapped (entry 86 says why nothing public does it). A runtime that renames
  the field or drops the export fails every server handshake on Linux with an `AuthenticationException` naming §4.2,
  rather than issue a ticket; the two `OnLinux` facts of `TlsConformanceTests` tell first.
- **Evidence:** `Axiom.Mesh.Core/Transport/OpenSslTickets.cs`.
- **Tolerated because:** the field has the same name in .NET 8 and .NET 10, the export exists since OpenSSL 1.1.1 (where
  TLS 1.3 begins), and the failure is loud and closed.
- **Paid in:** when `SslServerAuthenticationOptions` gains a ticket switch (dotnet/runtime), or when the CI scan runs on
  a runtime where `OnLinuxTheRecordCountSeesTheTwoTicketsABareSslStreamServerStillIssues` fails, which means
  `AllowTlsResume = false` suffices: drop `OpenSslTickets`, `FirstWriteHook` and this entry.
