# Security

How Axiom.Mesh meets the threat model of [DESIGN.md §14](DESIGN.md), SPEC.md §13 and LOCAL-SPEC.md §13: who may attack
it and how, what stops each attack, where every check lives, what the type system proves about it, and the test or the
workflow that proves it does what the specification says. ARCHITECTURE.md rule 9 applies to every line: a claim without
the test that proves it is not made here, and `SecurityDocTests` (in `ReadmeSamples.Tests`) fails when a row names a
test or a workflow that does not exist.

## Threat model

The assets are a user's Packages (what they say and who may read them), the user key (the one long-lived secret: it
signs Rosters, Manifests and Grants) and the device keys. The trust root is the Roster, signed by the user key; there is
no certificate authority and no server.

| Threat | What it can try | What stops it | Proved by |
|---|---|---|---|
| A LAN attacker, passive | read the traffic | every connection is mutual TLS 1.3 with a forward-secret AEAD suite (SPEC.md §4.2) | `Integration.Tests: TlsConformanceTests.ARealNodeNegotiatesTls13WithAnAeadSuiteAndTheMeshAlpn`, `tls-scan.yml` |
| A LAN attacker, active | pose as a known device, or downgrade the connection | the peer's key must be in a current Roster of a known user (SPEC.md §4.3); no TLS before 1.3 is accepted; no session ticket, so no resumption to replay | `Core.Tests: HandshakeTests.APeerWhoseKeyIsInNoRosterIsClosedWithoutAProtocolByte`, `Integration.Tests: TlsConformanceTests.AClientThatOffersOnlyTls12IsRefused`, `Core.Tests: HandshakeTests.NoSessionTicketsAndNoEarlyData` |
| A LAN attacker, flooding | exhaust connections, or lure the node into dialling | at most 4 connections per address; a 10-second handshake deadline; a device of no known user is never dialled; no announcement makes discovery throw | `Core.Tests: AdmissionTests.AdmissionPerAddressIs4`, `Core.Tests: HandshakeTests.AHandshakeWithoutAClientCertificateOrWithoutAlpnFails_AndCompletesWithin10Seconds`, `Core.Tests: PeerTableTests.AnAnnouncementOfADeviceOfNoKnownUserIsNeverDialled`, `Discovery.Tests: ParserFuzzTests.MutatedDnsPacketsAreReadOrRefusedButNeverThrow_Seed42` |
| A malicious peer (a device that passed the handshake) | forge or alter objects, write without a grant, sort its Entries last for ever | nothing is stored, served or forwarded without its hash, its form and its signer checked; a writer must be the owner or a Write grantee; a clock more than 2^32 above its parents is faulty, also when the parents arrive later | `Model.Tests: StorableObjectSurfaceTests.NoStorableObjectCanBeMadeOutsideModelWithoutVerification`, `Model.Tests: AdmissionTests.AnEntryIsValidOnlyFromADeviceOfTheOwnerOrAWriteGrantee_AndStaysValidAfterItsDeviceIsRemoved`, `Core.Tests: FaultyContentTests.AForgedClockMoreThan2Pow32AboveItsParentIsDroppedAsFaultyAndNeverForwarded`, `Core.Tests: LateParentTests.AClockGapAbove2Pow32IsFaultyWhicheverArrivesFirst` |
| A malicious peer, reading | obtain a Package it was not granted, or what it references | every serving node checks visibility itself for listings, `Have`, `Want` answers and Rosters (SPEC.md §9.7, §9.8) | `Core.Tests: ListingTests.TheListingHoldsExactlyThePackagesWhoseOwnerOrCurrentGranteeIsThePeersUser`, `Core.Tests: ServingTests.AChunkIsServedThroughAnyVisiblePackageThatReferencesIt_ElseNotFound` |
| A malicious peer, exhausting | oversize frames, slow frames, too many wants, unverifiable objects | every bound of the specification is enforced where the bytes enter; held objects are bounded per session and in total | `Protocol.Tests: LimitTests.EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit_AViolationInAMessageMalformedInAnObject`, `Core.Tests: IntakeTests.ObjectsApplyInTheOrderRosterManifestGrantEntryPayloadWhateverTheirArrival_Bounded8MiBPerSession64MiBInAll`, `Protocol.Tests: CodecFuzzTests.MutatedAmpFramesNeverThrowOrHang` |
| A process of another operating-system user on the same machine | talk to the Host, read its keys or its configuration | the pipe admits the Host's own user only, by its access list or socket mode and by the peer's process identity; key files are DPAPI-wrapped (Windows) or 0600 in a 0700 directory (Linux); `config.json` (which holds the service token) is the user's alone | `Integration.Tests: PipeTests.OnWindowsAnotherAccountIsRefused`, `Integration.Tests: PipeTests.OnLinuxAnotherUserIsRefusedByTheSocketsModeAndByProcessIdentity`, `Host.Tests: FileKeyStoreTests.OnWindowsTheFileHoldsTheKeyWrappedWithDpapi_NeverInTheClear`, `Host.Tests: FileKeyStoreTests.OnLinuxTheDirectoryIs0700AndEachFile0600_AWiderDirectoryIsRefused`, `Host.Tests: HostConfigurationTests.TheConfigurationFileIsTheUsersAlone_0600` |
| A process of another machine | reach the pipe over the network | the Windows pipe refuses remote clients; the Linux socket is a file | `Integration.Tests: PipeTests.OnWindowsAConnectionThroughTheMachinesNameIsRefused` |
| A granted user who leaks | copy what it was allowed to read | not defended: a grant bounds who the mesh serves, not what a reader does (SPEC.md §13); a revocation stops all later serving and reaches the revoked user | `Core.Tests: ServingTests.ARevokedUserIsNoLongerListedOrServed_ButReceivesTheRevocationGrant` |
| A compromised device | keep talking to the mesh, write as the user | its user removes it from the Roster: every node that holds the new Roster closes it and refuses it for ever, across restarts; what it wrote before stays valid; a device enrolled by pairing never holds the user key, so a stolen phone cannot sign a Roster | `Core.Tests: EstablishmentTests.AnEstablishedSessionOfADeviceRemovedLaterIsClosedWithByeUnauthorized`, `Testing.Tests: MeshScenarios.ARemovedDeviceIsClosedAndRefusedForEver`, `Testing.Tests: MeshScenarios.AnEnrolledDeviceHoldsTheUsersIdentityButNotItsKey_AndReplicatesItsPackages` |

## What each layer guarantees

| Check | Lives in | Proven by the type system | Proved by |
|---|---|---|---|
| SPEC §4.2: TLS 1.3 only, a client certificate, forward-secret AEAD suites, ALPN required, no session tickets, no 0-RTT | the mesh transport's TLS options (`MeshTlsLayer`) | — | `Core.Tests: HandshakeTests.NoSessionTicketsAndNoEarlyData`, `Integration.Tests: TlsConformanceTests.ARealNodeNegotiatesTls13WithAnAeadSuiteAndTheMeshAlpn`, `Integration.Tests: TlsConformanceTests.AConnectionWithoutTheAlpnIsClosedWithoutAProtocolByte`, `tls-scan.yml` |
| SPEC §4.3: the peer's key is in a current roster and not removed | the TLS callback of the mesh transport, over the trust snapshot | the peer identity exists only on success; a session without it is closed | `Core.Tests: HandshakeTests.APeerWhoseKeyIsInNoRosterIsClosedWithoutAProtocolByte`, `Core.Tests: HandshakeTests.ARemovedDeviceIsRefusedAtTheHandshake` |
| SPEC §7.1: `Hello` version, device id = H(TLS key), known user, device in that user's current roster | the pure Hello rule | the session is Established only on its `Ok`; before that only `Hello`, `Bye`, `Ping`, `Pong` parse | `Core.Tests: HelloTests.ADeviceIdThatIsNotTheHashOfTheTlsKeyOrNotInTheUsersCurrentRosterIsByeUnauthorized`, `Protocol.Tests: SessionStateTests.BeforeThePeersHelloOnlyHelloAndByeAreAccepted_AndNoHelloWithin10SecondsCloses` |
| SPEC §7.6: an object's hash is H(object) | hashed-object verification | nothing parses without a verified hash | `Protocol.Tests: ObjectMessageTests.AnObjectWhoseHashIsNotItsSha256IsAViolation` |
| SPEC §8: well-formedness | the object parser | a parsed object exists only when well-formed | `Model.Tests: ObjectBoundsTests.EveryObjectBoundIsAcceptedAtTheLimitAndMalformedOneOver` |
| SPEC §8.6–§8.9: signatures; §9.6: the writer's right | signed-object verification and Entry admission | a storable object is minted only there; the store and the `Object` message accept nothing else | `Model.Tests: StorableObjectSurfaceTests.NoStorableObjectCanBeMadeOutsideModelWithoutVerification`, `Model.Tests: AdmissionTests.AnEntryIsValidOnlyFromADeviceOfTheOwnerOrAWriteGrantee_AndStaysValidAfterItsDeviceIsRemoved` |
| SPEC §8.6: an Entry stored before its parents is checked when the last of them arrives; a faulty one leaves the store and its device's next Entry becomes the Head | the store writer, with a row per missing parent | — | `Core.Tests: LateParentTests.AnEntryStoredBeforeItsParentIsCheckedWhenTheParentArrives_AndAFaultyOneIsNoLongerStoredOrAHead` |
| SPEC §9.7, §9.8: visibility | one visibility rule behind listing, `Have`, `Want` answers and Roster serving | an answer takes a visibility decision as a token | `Core.Tests: ListingTests.TheListingHoldsExactlyThePackagesWhoseOwnerOrCurrentGranteeIsThePeersUser`, `Core.Tests: ServingTests.AChunkIsServedThroughAnyVisiblePackageThatReferencesIt_ElseNotFound` |
| SPEC §8.8: permanent removal | the store writer, when it applies a Roster | the trust snapshot exposes removal, never a flag to clear | `Model.Tests: RosterTests.RemovalIsPermanent_AnOmittedDeviceCountsAsRemoved_ADeviceIdInTwoUsersRostersIsRemovedInTheLater` |
| SPEC §4.5 duplicates, §6.2 refusals, §12.1 backoff | the peer table, with pure duplicate and backoff rules | — | `Model.Tests: DuplicateSessionTests.Decide`, `Core.Tests: BackoffTests.AFailedDialWaits10SecondsThenDoublesToAtMost5Minutes` |
| LOCAL §2: pipe hardening and process identity | the Host's pipe listeners (`PipeHost`) | adopting a pipe connection requires a process identity | `Host.Tests: PipeAdmissionTests.Decide_AnotherOsUserIsRefused_TheServedUserAdmitted_AnAllowListedUserAdmittedForASystemService`, `Integration.Tests: PipeTests.TheHostLearnsTheClientsPidAndUserFromThePlatform` |
| LOCAL §4.2: roles, names and tokens (in-process, spawned, external) | the local port's Hello checks | a connection's role is fixed at Ready; the dispatcher table states the roles of each request | `Core.Tests: LocalHelloTests.Refusals`, `Core.Tests: LocalHelloTests.AnInProcessNameIsNeverGrantedOnThePipe_ASpawnedOrExternalNameNeedsItsToken` |
| LOCAL §2.2: user-key requests logged and confinable; `ConfirmPairing` counts as one in every mode but `ShowDevice` | the dispatcher's pre-check, with the process identity and the running pairing's mode | — | `Core.Tests: UserKeyRequestTests.EveryUserKeyRequestIsLoggedWithPidAndPath_AndOneFromAnUnlistedExecutableIsNotPermitted`, `Core.Tests: DispatchRuleTests.ConfirmPairingSpendsTheUserKeyInEveryModeButShowDevice` |
| LOCAL §13: no key crosses the pipe | the local protocol's records | no record type can carry a private key | `Local.Tests: NoKeyCrossesThePipeTests.NoRecordHasAFieldThatCanHoldAPrivateKey` |
| DESIGN §11: the Host parses no payload and opens no path a client names | the Host reads only the signed envelope; files are cut and written by the client's helpers, as the client's user | no request has a path field; Core and Host reference no payload type | `ReadmeSamples.Tests: ContentBlindHostTests.NoPayloadTypeIsReferencedByCoreOrHost`, `Local.Tests: NoRequestNamesAPathTests.NoRequestHasAFieldThatNamesAPathForTheHostToOpen` |

## Keys

The device and user keys are PKCS#8 files in the Host's key folder. The private halves never leave the Host process:
signing happens inside it (the user key only behind `MeshNode.WithUserKeyAsync`), and the local protocol has no record
that could carry one. The device certificate is rebuilt from the device key at every start.

| Key fact | Proved by |
|---|---|
| On Windows a key file holds the key wrapped with DPAPI for the current user, never in the clear. | `Host.Tests: FileKeyStoreTests.OnWindowsTheFileHoldsTheKeyWrappedWithDpapi_NeverInTheClear` |
| On Linux the key folder is 0700 and each file 0600; a wider folder is refused at start. | `Host.Tests: FileKeyStoreTests.OnLinuxTheDirectoryIs0700AndEachFile0600_AWiderDirectoryIsRefused` |
| A key written is read back exactly; a key never written is absent, never invented. | `Host.Tests: FileKeyStoreTests.AKeyWrittenIsReadBackExactly_AndAKeyNeverWrittenIsAbsent` |
| No key crosses the pipe. | `Local.Tests: NoKeyCrossesThePipeTests.NoRecordHasAFieldThatCanHoldAPrivateKey` |
| A request that needs the user key on a device that does not hold it is `UserKeyAbsent` and changes nothing; an enrolled device never receives the user key. | `Core.Tests: UserRequestTests.RemovingThisDeviceIsInvalid_AnotherUsersIsNotFound_WithoutTheUserKeyItIsUserKeyAbsent`, `Core.Tests: PairingTests.AddDeviceEnrolsASecondDevice_ItHoldsTheUsersIdentityAndRosterSeqPlus1OnBoth_AndEstablishesWithTheFirst` |
| The `cli` service token lives in `config.json`, readable by the user alone, and is compared in constant time. | `Host.Tests: HostConfigurationTests.TheConfigurationFileIsTheUsersAlone_0600`, `Local.Tests: HelloTests.ATokenParsesFromItsHexForm_ComparesInFixedTime_AndNeverPrintsItself` |

## The pipe

The local port trusts the operating-system user, as ssh-agent and Docker do (LOCAL-SPEC.md §2.2): there is no password
and no network port. The listeners were hardened in M8 and proved end to end in M11.

| Pipe fact | Proved by |
|---|---|
| Windows: each pipe instance is created with `CreateNamedPipeW`, first instance only, remote clients rejected, and an owner-only protected access list; a second daemon on the same pipe does not start (exit 3). | `Integration.Tests: PipeTests.OnWindowsThePipesAccessListAllowsTheHostsAccountAlone`, `Integration.Tests: PipeTests.OnWindowsAConnectionThroughTheMachinesNameIsRefused`, `Host.Tests: RealPipeTests.AClientOfTheSameUserCreatesAnIdentityOverTheRealPipe_ItSurvivesARestart_ASecondDaemonOnThePipeDoesNotStart` |
| Linux: the socket is mode 0600; a stale socket is taken over, a live one never. | `Integration.Tests: PipeTests.OnLinuxTheSocketIs0600_AStaleOneIsTakenOver_ALiveOneIsNot` |
| The Host learns the client's process id, executable and user from the operating system (`GetNamedPipeClientProcessId` and the client's token; `SO_PEERCRED` and `/proc/<pid>/exe`), never from the client. | `Integration.Tests: PipeTests.TheHostLearnsTheClientsPidAndUserFromThePlatform` |
| Another operating-system user is refused, by the access list or socket mode and again by process identity. | `Host.Tests: PipeAdmissionTests.Decide_AnotherOsUserIsRefused_TheServedUserAdmitted_AnAllowListedUserAdmittedForASystemService`, `Integration.Tests: PipeTests.OnLinuxAnotherUserIsRefusedByTheSocketsModeAndByProcessIdentity` |
| An in-process identity is never admitted on the pipe. | `Host.Tests: PipeAdmissionTests.AnInProcessIdentityIsNeverAdmittedOnThePipe` |

## TLS parameters and the scan that enforces them

Every mesh connection is a fresh `SslStream` with a full handshake (`MeshTlsLayer.ServerOptions` and `ClientOptions`).
The workflow `tls-scan.yml` scans a real node from outside on every change to the transport and weekly: testssl.sh
(`--protocols --each-cipher --server-defaults`, presenting the certificate of a device the node trusts, so its handshakes
complete) and one `openssl s_client -alpn axiom-mesh/1` handshake, judged by `Conformance/tls/check.py`, which fails on
anything but TLS 1.3, a suite outside the AEAD list below, a session ticket or resumption, early data, or a missing
ALPN `axiom-mesh/1`, and checks its own rules first (`--self-test`). The scan target is
`Conformance/Axiom.Mesh.TlsScan`. check.py has run on CI only: the development machine has no Python.

| Parameter | Value | Proved by |
|---|---|---|
| Protocol | TLS 1.3 only; a TLS 1.2 client is refused | `Integration.Tests: TlsConformanceTests.AClientThatOffersOnlyTls12IsRefused`, `tls-scan.yml` |
| Cipher suites | TLS_AES_128_GCM_SHA256, TLS_AES_256_GCM_SHA384, TLS_CHACHA20_POLY1305_SHA256 (the platform's TLS 1.3 set; all forward-secret AEAD) | `Integration.Tests: TlsConformanceTests.ARealNodeNegotiatesTls13WithAnAeadSuiteAndTheMeshAlpn`, `tls-scan.yml` |
| Client certificate | required; validated by the key alone against the rosters (no chain, name, revocation or validity checks) | `Core.Tests: HandshakeTests.AHandshakeWithoutAClientCertificateOrWithoutAlpnFails_AndCompletesWithin10Seconds` |
| ALPN | `axiom-mesh/1` (and `axiom-mesh-pair/1` only while a pairing waits); a connection without it is closed without a protocol byte | `Integration.Tests: TlsConformanceTests.AConnectionWithoutTheAlpnIsClosedWithoutAProtocolByte`, `Core.Tests: HandshakeTests.UnderTheMeshAlpnAnUnknownKeyIsRefusedWhileAPairingWaits_UnderThePairingAlpnItIsLetThrough` |
| Session tickets, resumption, 0-RTT | none (`AllowTlsResume = false` on both roles; without a ticket there is no early data) | `Core.Tests: HandshakeTests.NoSessionTicketsAndNoEarlyData`, `tls-scan.yml` |
| Handshake deadline | 10 seconds on the node's clock | `Core.Tests: HandshakeTests.AHandshakeWithoutAClientCertificateOrWithoutAlpnFails_AndCompletesWithin10Seconds` |

## Out of scope

- **Traffic analysis.** Discovery announces that a node exists and its ids; a passive observer learns who talks to whom
  and how much (SPEC.md §13).
- **A granted user leaking** what it may read; and a hostile program running as the same operating-system user, which
  the pipe trusts as that user by design (LOCAL-SPEC.md §2.2). The user-key requests are logged and can be confined to
  listed executables (`userKeyRequests.confineTo`), which narrows but does not close that door.
- **Everything deferred in plan decision D7 and STRUCTURAL-DEBT.md:** encryption at rest (entry 9), key backup and the
  recovery from a lost user key (entry 4), spawned services (entry 32), service calls (entry 7), an installer that
  registers the daemon as a service (entry 10).

## Reporting a vulnerability

Report it privately through GitHub's security advisories on the repository (Security, "Report a vulnerability"), not
in a public issue. Include the version (`axiom-meshd version`), the platform, and the steps that show it.
