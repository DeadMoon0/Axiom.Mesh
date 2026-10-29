# Security

How Axiom.Mesh meets the threat model of [DESIGN.md §14](DESIGN.md), SPEC.md §13 and LOCAL-SPEC.md §13: each check,
where it lives, what the type system proves about it, and the test that proves it does what the specification says.
A check whose test does not exist yet says `planned:`; until then it is a design, not a property (ARCHITECTURE.md
rule 9). This document is completed in milestone M12, with the TLS scan.

## Checks

| Check | Lives in | Proven by the type system | Test |
|---|---|---|---|
| SPEC §4.3: the peer's key is in a current roster and not removed | the TLS callback of the mesh transport, over the trust snapshot | the peer identity exists only on success; a session without it is closed | planned: `Core.Tests: HandshakeTests.APeerWhoseKeyIsInNoRosterIsClosedWithoutAProtocolByte` |
| SPEC §7.1: `Hello` version, device id = H(TLS key), known user, device in that user's current roster | the pure Hello rule | the session is Established only on its `Ok`; before that only `Hello`, `Bye`, `Ping`, `Pong` parse | planned: `Core.Tests: HandshakeTests.ADeviceIdThatIsNotTheHashOfTheTlsKeyOrNotInTheUsersCurrentRosterIsByeUnauthorized` |
| SPEC §7.6: an object's hash is H(object) | hashed-object verification | nothing parses without a verified hash | planned: `Model.Tests: HashedObjectTests.AnObjectWhoseHashDoesNotMatchIsRefusedBeforeItIsParsed` |
| SPEC §8: well-formedness | the object parser | a parsed object exists only when well-formed | planned: `Model.Tests: TreeTests.ATreeWhoseEntriesAreNotSortedByNameAsByteStringsIsMalformed` |
| SPEC §8.6–§8.9: signatures; §9.6: the writer's right | signed-object verification and Entry admission | a storable object is minted only there; the store and the `Object` message accept nothing else | planned: `Model.Tests: EntryAdmissionTests.AnEntryIsValidOnlyFromADeviceOfTheOwnerOrAWriteGrantee_AndStaysValidAfterItsDeviceIsRemoved` |
| SPEC §9.7, §9.8: visibility | one visibility rule behind listing, `Have`, `Want` answers and Roster serving | an answer takes a visibility decision as a token | planned: `Core.Tests: VisibilityTests.TheListingHoldsExactlyThePackagesWhoseOwnerOrCurrentGranteeIsThePeersUser` |
| SPEC §8.8: permanent removal | the store writer, when it applies a Roster | the trust snapshot exposes removal, never a flag to clear | planned: `Model.Tests: RosterTests.RemovalIsPermanent_AnOmittedDeviceCountsAsRemoved_ADeviceIdInTwoUsersRostersIsRemovedInTheLater` |
| SPEC §4.5 duplicates, §6.2 refusals, §12.1 backoff | the peer table, with pure duplicate and backoff rules | — | planned: `Core.Tests: PeerTableTests.AFailedDialWaits10SecondsThenDoublesToAtMost5Minutes` |
| LOCAL §2: pipe hardening and process identity | the Host's pipe listeners | adopting a pipe connection requires a process identity | planned: `Host.Tests: PipeAdmissionTests.Decide` |
| LOCAL §4.2: roles, names and tokens (in-process, spawned, external) | the local port's Hello checks | a connection's role is fixed at Ready; the dispatcher table states the roles of each request | planned: `Core.Tests: HelloTests.Refusals` |
| LOCAL §13: user-key requests logged and confinable | the dispatcher's pre-check, with the process identity | — | planned: `Core.Tests: UserKeyRequestTests.EveryUserKeyRequestIsLoggedWithPidAndPath_AndOneFromAnUnlistedExecutableIsNotPermitted` |
| LOCAL §13: no key crosses the pipe | the local protocol's records | no record type can carry a private key | planned: `Local.Tests: NoKeyCrossesThePipeTests.NoRecordHasAFieldThatCanHoldAPrivateKey` |
| SPEC §4.2: TLS 1.3 only, AEAD suites, no session tickets, no 0-RTT, ALPN required | the mesh transport's TLS options | — | planned: `tls-scan.yml` (testssl.sh against the mesh port, M12) |

## Keys

The device and user keys are PKCS#8 files in the Host's key folder: wrapped with DPAPI for the current user on
Windows; mode 0600 in a 0700 folder on Linux, and the Host refuses to start when either is wider. The user key never
leaves the Host. The device certificate is rebuilt from the device key at every start.

## Out of scope

Traffic analysis; a granted user leaking what it may read; a hostile program running as the same OS user (the pipe
trusts the OS user, as ssh-agent and Docker do: LOCAL-SPEC.md §2.2); and everything deferred in STRUCTURAL-DEBT.md,
encryption at rest and key backup included.

## Reporting a vulnerability

Report it privately through GitHub's security advisories on the repository, not in a public issue.
