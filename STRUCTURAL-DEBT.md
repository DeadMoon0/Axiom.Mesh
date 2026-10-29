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
- **Instead:** multicast is filtered on hosted CI runners; the mDNS shell is exercised by hand only.
- **Evidence:** fundamentals plan, decision D7; M4 records mDNS as manual.
- **Tolerated because:** the DNS-SD codec is pure and fully tested; broadcast and static peers are tested on loopback.
- **Paid in:** not planned for CI; the manual check is part of `ACCEPTANCE.md` (M13/M14).

## 3. PIPE-ACCESS.md is not written

- **Rule:** LOCAL-SPEC.md §2.4 defers the exact ACL, socket modes, service account and allow list to `PIPE-ACCESS.md`.
- **Instead:** the document does not exist.
- **Evidence:** `Documentation/` holds no `PIPE-ACCESS.md`.
- **Tolerated because:** no pipe listener exists yet.
- **Paid in:** M8, with the pipe listeners.

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
- **Instead:** requests outside the phase-1 set of the fundamentals plan §2.5 answer `UnknownRequest`.
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

- **Rule:** SPEC.md §8.6 and §12.2: once its parents are held, an Entry whose parent is of another Package, whose clock
  is not above every parent's, or exceeds the greatest by more than 2^32, is faulty and not forwarded.
- **Instead:** intake admits an Entry on its signature and writer (`EntryAdmission`) alone; `Lamport.TryFindParentFault`
  is not consulted yet.
- **Evidence:** `Axiom.Mesh.Core/Intake/IntakeRules.cs`.
- **Tolerated because:** nothing is forwarded before replication exists, and the check needs the history walk that
  wants the parents in the first place.
- **Paid in:** M7, with walking history (SPEC.md §10.3).

## 18. Evicting an Entry forgets its Head without electing the next one

- **Rule:** SPEC.md §9.3: a node keeps, per Package and device, the stored Entry with the highest clock.
- **Instead:** when an Entry is evicted (possible only for a Package no longer followed, whose Entries are unpinned),
  its `Heads` row is deleted and no other stored Entry of the device takes its place until one is stored again.
- **Evidence:** `Axiom.Mesh.Core/Store/StoreBatch.cs`, `UnlinkAsync`.
- **Tolerated because:** an unfollowed Package is neither listed nor replicated; following it again fetches its Heads.
- **Paid in:** M7, with the listing (`PackageState.heads` recomputed from the stored Entries on follow).

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
