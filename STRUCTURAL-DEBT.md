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
