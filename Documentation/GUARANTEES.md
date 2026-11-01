# Guarantees

What Axiom.Mesh promises, each with the test that proves it. **A promise without a test is not made:** a row whose
test does not exist yet would say `planned:` before the test that will prove it (no row does since 0.1.0). Test names
are `<test project>: <class>.<method>`; projects live under `UnitTests/` (or `IntegrationTests/`), `Shared` is
`UnitTests/Shared`, whose classes every suite derives. `GuaranteeTests` (in `ReadmeSamples.Tests`) fails when a row
names no test, or when a proved row names a test that does not exist.

## Trust and peers

| Promise | Proved by |
|---|---|
| A peer whose key is in no current roster is closed before a protocol byte is sent. | `Core.Tests: HandshakeTests.APeerWhoseKeyIsInNoRosterIsClosedWithoutAProtocolByte`, `Core.Tests: EveScenarioTests.EveIsClosedWithoutAByte_NeverDialled_AndNeverEstablished` |
| A removed device stays removed in every later roster of its user, whatever that roster says; an omitted device counts as removed. | `Model.Tests: RosterTests.RemovalIsPermanent_AnOmittedDeviceCountsAsRemoved_ADeviceIdInTwoUsersRostersIsRemovedInTheLater` |
| A removed device is closed and refused for ever at the handshake. | `Core.Tests: HandshakeTests.ARemovedDeviceIsRefusedAtTheHandshake`, `Core.Tests: EstablishmentTests.AnEstablishedSessionOfADeviceRemovedLaterIsClosedWithByeUnauthorized` |
| No object becomes storable without its hash, its form and its signer checked: a signed object outside Model cannot be made storable without verification, and one flipped bit makes it invalid. | `Model.Tests: StorableObjectSurfaceTests.NoStorableObjectCanBeMadeOutsideModelWithoutVerification`, `Model.Tests: SignedObjectTests.OneFlippedBitInTheBodyMakesAnEntryManifestRosterOrGrantInvalidAndRenamesIt` |
| An Entry is stored only from a device of the owner or of a current write grantee, and stays valid once stored. | `Model.Tests: AdmissionTests.AnEntryIsValidOnlyFromADeviceOfTheOwnerOrAWriteGrantee_AndStaysValidAfterItsDeviceIsRemoved` |
| Nothing unverified is stored, served or forwarded: an object is checked against its hash, its form and its signer before anything acts on it; faulty content is dropped and never forwarded; only the store's own bytes go out as stored objects. | `Core.Tests: IntakeTests.ObjectsApplyInTheOrderRosterManifestGrantEntryPayloadWhateverTheirArrival_Bounded8MiBPerSession64MiBInAll`, `Core.Tests: ServingTests.AnObjectWhoseHashDoesNotMatchIsAViolation_AnUnwantedOneIsDroppedExceptTheSendersOwnRoster`, `Core.Tests: FaultyContentTests.AForgedClockMoreThan2Pow32AboveItsParentIsDroppedAsFaultyAndNeverForwarded`, `Core.Tests: SendPathTests.OnlyTheStoreSendPathMakesObjectMessagesFromStoredBytes` |
| A Package is listed, claimed and served only to its owner and its current grantees. | `Core.Tests: ListingTests.TheListingHoldsExactlyThePackagesWhoseOwnerOrCurrentGranteeIsThePeersUser`, `Core.Tests: ServingTests.AChunkIsServedThroughAnyVisiblePackageThatReferencesIt_ElseNotFound` |
| A revocation reaches the revoked user, who is then no longer listed or served. | `Core.Tests: ServingTests.ARevokedUserIsNoLongerListedOrServed_ButReceivesTheRevocationGrant` |
| Two nodes holding two sessions to each other decide which one to keep identically, without talking. | `Model.Tests: DuplicateSessionTests.Decide` |
| One session per device id: two nodes that connect twice keep the same one without talking. | `Core.Tests: DuplicateSessionTests.BothDialAtOnce_ExactlyOneSessionPerDeviceSurvives_TheSameConnectionOnBothNodes`, `Core.Tests: DuplicateSessionTests.TwoSessionsDialledByDifferentDevices_TheOneDialledByTheLowerDeviceIdIsKept` |
| On a real socket every mesh connection negotiates TLS 1.3 with an AEAD suite and the ALPN `axiom-mesh/1`; a client limited to TLS 1.2 gets no handshake, and a connection without the ALPN is closed before a protocol byte. | `Integration.Tests: TlsConformanceTests.ARealNodeNegotiatesTls13WithAnAeadSuiteAndTheMeshAlpn`, `Integration.Tests: TlsConformanceTests.AClientThatOffersOnlyTls12IsRefused`, `Integration.Tests: TlsConformanceTests.AConnectionWithoutTheAlpnIsClosedWithoutAProtocolByte` |

## Bounds

| Promise | Proved by |
|---|---|
| Every bound of an AMP/1 object is enforced: one over makes the object malformed, at the bound it is accepted. | `Model.Tests: ObjectBoundsTests.EveryObjectBoundIsAcceptedAtTheLimitAndMalformedOneOver` |
| Every bound of AMP/1 is enforced: one over is refused, at the bound is accepted, as a violation in a message and as malformed in an object. | `Protocol.Tests: LimitTests.EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit_AViolationInAMessageMalformedInAnObject` |
| Every bound is a named constant with the specification's value. | `Model.Tests: LimitsDocumentTests.EveryLimitOfTheSpecIsANamedConstantWithTheSpecsValue`, `Model.Tests: LimitsTests.EveryLimitHasTheValueItsSectionStates` |
| Every bound of the local protocol is enforced, one over refused and at the bound accepted. | `Local.Tests: LimitTests.EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit`, `Core.Tests: LocalHelloTests.Refusals`, `Core.Tests: LocalHelloTests.NoHelloWithin10SecondsClosesWithoutAFrame_A17thWaiterClosesTheLongestWaiting`, `Core.Tests: LocalConnectionTests.ACorrelationOf0OrStillOutstanding_OrA65thOutstandingRequest_IsAViolation`, `Core.Tests: OperationTests.CancelOperationDrainAndAClosedConnectionEndAnOperationCancelled_A17thRunningIsBusy_The49thEndedDropsTheOldest` |
| A slow local client loses its subscription, never the mesh: other subscriptions and requests continue. | `Core.Tests: SubscriptionTests.A257thEventOr16MiBLapsesOnlyThatSubscription_OthersAndRequestsContinue_64MiBUnacceptedIsByeUnresponsive` |

## The pipe

| Promise | Proved by |
|---|---|
| The pipe admits the Host's operating-system user only. | `Host.Tests: PipeAdmissionTests.Decide_AnotherOsUserIsRefused_TheServedUserAdmitted_AnAllowListedUserAdmittedForASystemService`, `Host.Tests: RealPipeTests.AClientOfTheSameUserCreatesAnIdentityOverTheRealPipe_ItSurvivesARestart_ASecondDaemonOnThePipeDoesNotStart` |
| Another operating-system user is refused, by the access list and by process identity, end to end: on Linux as user `nobody` against the socket's mode and its `SO_PEERCRED` check; on Windows by an owner-only, protected access list and by refusing remote clients. | `Integration.Tests: PipeTests.OnLinuxAnotherUserIsRefusedByTheSocketsModeAndByProcessIdentity`, `Integration.Tests: PipeTests.OnWindowsThePipesAccessListAllowsTheHostsAccountAlone`, `Integration.Tests: PipeTests.OnWindowsAConnectionThroughTheMachinesNameIsRefused` |
| The Host learns who connected to the pipe from the operating system, never from the client. | `Integration.Tests: PipeTests.TheHostLearnsTheClientsPidAndUserFromThePlatform` |
| Every request that spends the user key is logged with its process id and executable, and may be confined to listed executables. | `Core.Tests: UserKeyRequestTests.EveryUserKeyRequestIsLoggedWithPidAndPath_AndOneFromAnUnlistedExecutableIsNotPermitted` |
| An in-process service's name is never granted on the pipe; a spawned or external name only with its token. | `Core.Tests: LocalHelloTests.AnInProcessNameIsNeverGrantedOnThePipe_ASpawnedOrExternalNameNeedsItsToken` |
| No record of the local protocol can carry a private key. | `Local.Tests: NoKeyCrossesThePipeTests.NoRecordHasAFieldThatCanHoldAPrivateKey` |

## Replication

| Promise | Proved by |
|---|---|
| Every node that holds the same Entries orders them identically. | `Model.Tests: EntryOrderTests.EntriesOrderByClockThenDeviceIdThenHash_IdenticallyOnEveryNode` |
| The parents and clock chosen for a new Entry always make it well-formed and faultless, or writing stops with `ClockExhausted`. | `Model.Tests: ParentChoiceTests.Decide` |
| A `WriteEntry` with no parents produces an Entry that orders identically on every node. | `Core.Tests: ServiceRoleTests.WithNoParentsTheEntryVerifiesOrdersAndIsFaultless_ForTheFourParentCases_AndTwoParentsWhenTheHighestClockIs2Pow32Plus1AboveTheOwnHead` |
| A change crosses any number of hops without polling, and is never sent back to where it came from. | `Core.Tests: ReplicationScenarioTests.AChangeCrossesTwoHopsWithoutPolling`, `Core.Tests: ReplicationScenarioTests.AThirdNodeRelaysAndNothingIsSentBackToTheSource` |
| An Entry stored before its parents is checked when the last of them arrives; a faulty one leaves the store, and its device's next stored Entry becomes the Head, as it does whenever a Head is evicted. | `Core.Tests: LateParentTests.AnEntryStoredBeforeItsParentIsCheckedWhenTheParentArrives_AndAFaultyOneIsNoLongerStoredOrAHead`, `Core.Tests: LateParentTests.AChildOfTwoParentsIsCheckedWhenTheLastArrives_AndAFaultlessOneStays`, `Core.Tests: LateParentTests.EvictingADevicesHeadMakesItsNextStoredEntryTheHead` |
| The same scenario passes in memory and on sockets. | `Integration.Tests: ScenarioSuiteTests.TheSocketSuiteCompilesTheInMemorySuitesScenarioFileAndRunsEveryScenarioInIt` |

## Scenarios

Each scenario runs twice, from one source file: in memory (`Testing.Tests: InMemoryScenarios`) and on loopback sockets
with real TLS, a store and an index on disk and the real pipe (`Integration.Tests: SocketScenarios`).

| Promise | Proved by |
|---|---|
| Two users who pair see the same code, then know each other by the names their devices showed. | `Testing.Tests: MeshScenarios.TwoUsersWhoPairSeeTheSameCode_KnowEachOtherByName_AndEstablishOneSessionEach` |
| What one device writes reaches every device that may see it and follows it, through relays, and nothing is sent back to where it came from. | `Testing.Tests: MeshScenarios.AFollowerHoldsWhatTheOwnerWrote_AndAThirdDeviceRelaysWithoutSendingBackToTheSource` |
| What was written while a device was offline arrives once it is back, in both directions. | `Testing.Tests: MeshScenarios.WhatOneSideWroteWhileTheOtherWasOfflineArrivesOnceItIsBack_InBothDirections` |
| A revoked user keeps what it holds, receives the revocation, and is neither listed nor served afterwards. | `Testing.Tests: MeshScenarios.ARevokedUserKeepsWhatItHolds_ReceivesTheRevocation_AndIsNoLongerListedOrServed` |
| A removed device is closed and refused for ever, across restarts. | `Testing.Tests: MeshScenarios.ARemovedDeviceIsClosedAndRefusedForEver` |
| A device that connects twice keeps one session, the newer. | `Testing.Tests: MeshScenarios.ASecondConnectionOfOneDeviceReplacesTheFirst_WhichIsClosedAsADuplicate` |
| A client that stops reading loses its subscription; its connection and the mesh carry on. | `Testing.Tests: MeshScenarios.AClientThatStopsReadingLosesItsSubscription_TheConnectionAndTheMeshCarryOn` |
| A device of a user nobody knows is refused before a protocol byte and never dialled. | `Testing.Tests: MeshScenarios.EveIsRefusedBeforeAProtocolByte_AndNeverDialled` |
| Faulty content is dropped and never forwarded; an object with the wrong hash closes the session. | `Testing.Tests: MeshScenarios.AJumpedClockIsDroppedAsFaultyAndNeverForwarded_AWrongHashClosesTheSessionAsAViolation` |
| Under a tiny budget everything unpinned is evicted, and the publishing device keeps what it published. | `Testing.Tests: MeshScenarios.UnderATinyBudgetEverythingUnpinnedIsEvicted_TheOwnerKeepsWhatItPublished` |
| An enrolled device holds its user's identity, never the user key, and replicates the user's Packages. | `Testing.Tests: MeshScenarios.AnEnrolledDeviceHoldsTheUsersIdentityButNotItsKey_AndReplicatesItsPackages` |
| A restart keeps users, Packages and history, and replication resumes without pairing again. | `Testing.Tests: MeshScenarios.ARestartKeepsUsersPackagesAndHistory_AndReplicationResumes` |

## The packages

| Promise | Proved by |
|---|---|
| A package references only the layers below it; no service and no app reaches the engine or the daemon. | `Shared: ConventionTests.ReferencesOnlyTheStatedFamilyPackages`, `Shared: ServiceOrAppConventionTests.NoServiceOrAppReferencesCoreOrHost` |
| Every failure a package throws says who must act, whether retrying can succeed, and at least one step out. | `Shared: ExceptionRecoveryContractTests.EveryExceptionCarriesTheFullRecoveryContract`, `Model.Tests: FailureGuidanceTests.AFailureCannotBeMadeWithoutAMessageOrWithABlankStep` |
| A compiled consumer keeps working when a minor version adds members. | `Shared: PublicSurfaceTests.NoPublicMemberTakesAnOptionalParameter`, `Shared: PublicSurfaceTests.NoPublicPositionalRecordWithoutAClosedShape` |
| No package holds state shared by every node of a process. | `Shared: ConventionTests.HoldsNoMutableStaticState` |

## Axiom.Mesh.Discovery

| Promise | Proved by |
|---|---|
| A thousand announcements of devices nobody knows cost a bounded cache and nothing else. | `Discovery.Tests: AnnouncementCacheTests.AThousandUnknownAnnouncementsCostOnlyABoundedCache` |
| No datagram or packet on the LAN makes discovery throw: every byte string is read or refused. | `Discovery.Tests: ParserFuzzTests.MutatedDnsPacketsAreReadOrRefusedButNeverThrow_Seed42`, `Discovery.Tests: ParserFuzzTests.MutatedBroadcastDatagramsAreReadOrRefusedButNeverThrow_Seed42` |
| A node announces at most once per second per interface, however many queries it receives. | `Discovery.Tests: AnnounceGateTests.AtMostOneAnnouncementPerSecondPerInterface`, `Discovery.Tests: AnnounceGateTests.AFloodOfQueriesIsAnsweredOncePerSecond` |
| Nothing from the network can pose as a static peer: no parser yields an all-zero id. | `Discovery.Tests: BroadcastDatagramTests.ADatagramClaimingAZeroIdOrPortZeroIsIgnored`, `Discovery.Tests: DnsSdRecordTests.ARecordWhoseIdsOrInstanceNameAreNotOfTheSpecifiedFormIsIgnored`, `Discovery.Tests: StaticPeerTests.AStaticPeerAnnouncementHasAllZeroIds` |
| An unknown device found by discovery is never dialled. | `Core.Tests: PeerTableTests.AnAnnouncementOfADeviceOfNoKnownUserIsNeverDialled` |

## Axiom.Mesh.Protocol

| Promise | Proved by |
|---|---|
| A session accepts nothing but `Hello` and `Bye` before the peer's `Hello`, and a peer that sends no `Hello` within 10 seconds is closed. | `Protocol.Tests: SessionStateTests.BeforeThePeersHelloOnlyHelloAndByeAreAccepted_AndNoHelloWithin10SecondsCloses` |
| A peer's `Hello` is refused with `Bye(Unauthorized)` unless its version is 1, its device id is the hash of its TLS key, and its device is a current device of a known user's Roster. | `Protocol.Tests: HelloRuleTests.AHelloThatFailsACheckIsByeUnauthorized` |
| A received object is checked against its hash before anything acts on it, and decoding it copies nothing; only a verified object can be sent. | `Protocol.Tests: ObjectMessageTests.AnObjectWhoseHashIsNotItsSha256IsAViolation`, `Protocol.Tests: ObjectMessageTests.AnObjectIsSentOnlyFromAStorableObjectAndItsBytesAreNotCopied`, `Protocol.Tests: AllocationTests.DecodingA1MiBObjectFrameAllocatesOnlyTheMessage` |
| A 1.x peer's unknown extension messages are ignored; any other unknown message id closes the session. | `Protocol.Tests: FrameTests.AnUnknownIdBetween17And255IsIgnored_0OrAbove255IsAViolation` |
| A silent session is pinged after 60 seconds and closed after 5 minutes without receiving. | `Protocol.Tests: LivenessTests.PingAfter60SecondsOfSilence_ByeIdleAfter5MinutesWithoutReceiving` |
| No mutated frame of either protocol makes a decoder throw or hang. | `Protocol.Tests: CodecFuzzTests.MutatedAmpFramesNeverThrowOrHang`, `Protocol.Tests: CodecFuzzTests.MutatedPairingFramesNeverThrowOrHang` |
| A pairing completes only when both users confirmed, the short authentication string differs when any key is substituted, and a pairing expires after 10 minutes. | `Protocol.Tests: SasTests.TheSasIsEqualOnBothSidesAndDiffersWhenEitherKeyIsSubstituted`, `Protocol.Tests: PairingMachineTests.ConfirmIsInvalidWhileWaiting_OkAndUnchangedWhileConfirming_ASecondPairingIsBusy_ExpiryAt10MinutesFails` |
| A device adopts a Roster only when its user's key signed it, it is newer, it lists the device, and the device's own user confirmed first; a removed device is never enrolled again. | `Protocol.Tests: PairingRulesTests.AnEnrolmentIsAcceptedOnlyAsTheUsersNewerRosterListingThisDevice`, `Protocol.Tests: PairingMachineTests.TheUserSideEnrolsTheDeviceOnlyAfterTheDevicesUserConfirmed`, `Protocol.Tests: PairingRulesTests.ADeviceOnceRemovedIsNeverEnrolledAgain_ACurrentOneIsKept` |

## Axiom.Mesh.Local

| Promise | Proved by |
|---|---|
| Every bound a frame of the local protocol carries is enforced where the bytes enter: one over is a violation from a client and a lost connection from the Host, at the bound it reads. | `Local.Tests: LimitTests.EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit`, `Local.Tests: BoundsTests.AFieldOutsideItsBoundsInARequestIsAViolation`, `Local.Tests: BoundsTests.AFieldOutsideItsBoundsInAResponseOrAnEventIsAHostDefect` |
| An id one side does not know never closes the connection: an unknown request is answered `UnknownRequest`, an unknown event discarded, an unknown callback answered. | `Local.Tests: UnknownIdTests.AnUnknownRequestIdIsAnsweredUnknownRequestAndTheConnectionStaysOpen`, `Local.Tests: UnknownIdTests.AnUnknownEventIsDiscardedByTheClient_TheNextOneArrives`, `Local.Tests: UnknownIdTests.AnUnknownCallbackIsAnsweredUnknownRequestByTheService` |
| A value a newer Host sends that this client does not know is kept, never a failure. | `Local.Tests: EnumToleranceTests.AnUnknownEnumeratedValueInAnEventIsReadAndKept` |
| A request the Host refuses is a status and an error in the client, never an exception. | `Local.Tests: LocalClientExchangeTests.ARequestIsAnsweredWithItsOkBodyAndARefusalIsAValue` |
| The client never sends a 65th outstanding request, and a reader that falls behind loses only its own subscription. | `Local.Tests: ClientLimitTests.AClientRefusesA65thOutstandingRequestBeforeSendingIt`, `Local.Tests: ClientLimitTests.AReaderThatFallsBehindLapsesOnlyItsOwnSubscription_OthersAndRequestsContinue` |
| Nothing of the engine or the daemon is reachable from the package every app and service references. | `Local.Tests: SurfaceTests.NoCoreTypeIsReachableFromLocalsPublicSurface` |

## Axiom.Mesh.Core

| Promise | Proved by |
|---|---|
| A crash or a failed write leaves an object absent or complete, never partial; a request that failed stored nothing. | `Core.Tests: StoreTests.AnInterruptedWriteLeavesNoObject_ACompleteOneIsFoundUnderItsHash` |
| A program reading an object directly keeps its bytes while the node evicts it; the node counts it evicted only once it is unlinked. | `Core.Tests: StoreTests.AnObjectOpenedFromTheStoreDirectoryKeepsItsBytesWhileEvicted_AndTheHostCountsItEvictedOnlyAfterUnlink` |
| A pinned object is never evicted, nor one a peer is still waiting for; an evicted object is never claimed again. | `Core.Tests: EvictionTests.UnpinnedObjectsGoLeastRecentlyUsedFirstUntilUnderBudget_AndAreNeverClaimedAfterwards`, `Core.Tests: EvictionTests.AnObjectPromisedWithHaveIsNotEvictedWhileAWantForItIsOutstanding` |
| Whatever this device published stays here until a peer has fetched it, whatever the retention policy. | `Core.Tests: PinTests.EveryObjectAnEntryWrittenHereReferencesIsPinnedOutboundUntilSentOnce` |
| A store sequence is never reused, survives a restart, and the store epoch changes only with a new database. | `Core.Tests: StoreSequenceTests.TheFirstEntryStoredIs1ThenPlusOne_AnEvictedEntryStoredAgainGetsANewSeq_AndItSurvivesARestart`, `Core.Tests: StoreSequenceTests.TheStoreEpochIsCreatedOnceWithTheDatabaseAndStableAcrossReopening` |
| An older Manifest, Roster or Grant never rolls back the current one, and a removed device stays removed across restarts. | `Core.Tests: TrustSnapshotTests.TheCurrentManifestRosterOrGrantIsTheHighestSeqTiesBrokenByTheLowerHash`, `Core.Tests: TrustSnapshotTests.RemovalIsPermanent_AnOmittedDeviceCountsAsRemoved_ADeviceIdInTwoUsersRostersIsRemovedInTheLater` |
| A reader of the store's changes that falls behind loses only its own subscription; the writer never waits for it. | `Core.Tests: ChangeBusTests.ASubscriberThatFallsBehindLapsesAlone_TheOthersReceiveEveryChangeSetInOrder` |
| Every connection is a full TLS 1.3 handshake on a fresh `SslStream`: no session tickets, no resumption, no renegotiation; a client certificate and the ALPN `axiom-mesh/1` are required. | `Core.Tests: HandshakeTests.NoSessionTicketsAndNoEarlyData`, `Core.Tests: HandshakeTests.AHandshakeWithoutAClientCertificateOrWithoutAlpnFails_AndCompletesWithin10Seconds` |
| A handshake that does not complete within 10 seconds on the node's clock is closed, and not before. | `Core.Tests: HandshakeTests.AHandshakeWithoutAClientCertificateOrWithoutAlpnFails_AndCompletesWithin10Seconds` |
| While a pairing waits, an unknown key is still refused under AMP/1's ALPN; only the pairing ALPN lets it through, and only then. | `Core.Tests: HandshakeTests.UnderTheMeshAlpnAnUnknownKeyIsRefusedWhileAPairingWaits_UnderThePairingAlpnItIsLetThrough` |
| A pairing stores nothing unless both users confirmed: a cancel, an expiry or an altered code leaves both nodes as they were. | `Core.Tests: PairingTests.ACancelOnEitherSideEndsBothCancelled_AndNothingIsStored`, `Core.Tests: PairingTests.ACodeAlteredToEvesKeyIsRefusedAtTheHandshake_TheShowingSideKeepsWaiting_NothingIsStored`, `Core.Tests: PairingTests.ConfirmIsInvalidWhileWaiting_OkAndUnchangedWhileConfirming_ASecondPairingIsBusy_ExpiryAt10MinutesFails` |
| A pairing connection never carries an AMP/1 frame, and a node serves one pairing connection at a time. | `Core.Tests: PairingTests.APairingConnectionCarriesNoAmp1Frame_AndASecondOneIsAnsweredBusy` |
| An enrolled device holds its user's identity but never the user key, and the user's other devices hold the Roster that lists it. | `Core.Tests: PairingTests.AddDeviceEnrolsASecondDevice_ItHoldsTheUsersIdentityAndRosterSeqPlus1OnBoth_AndEstablishesWithTheFirst` |
| A peer that closes before its `Hello` is a refusal: the node holds its next dial back 10 seconds, doubling to 5 minutes, and redials on its own. | `Core.Tests: EstablishmentTests.ACloseAfterTheHandshakeBeforeHelloCountsAsARefusalAndBacksOff`, `Core.Tests: BackoffTests.AFailedDialWaits10SecondsThenDoublesToAtMost5Minutes` |
| A peer's `Hello` that names another device or another user is answered `Bye(Unauthorized)` and counted against the device. | `Core.Tests: HelloTests.ADeviceIdThatIsNotTheHashOfTheTlsKeyOrNotInTheUsersCurrentRosterIsByeUnauthorized` |
| A node admits at most 4 connections per peer address. | `Core.Tests: AdmissionTests.AdmissionPerAddressIs4` |
| Two nodes with nothing to say keep their session on heartbeats alone. | `Core.Tests: LivenessTests.AnEstablishedSessionSurvivesFiveMinutesOfSilenceWithHeartbeats` |
| A stopping node ends every session with `Bye(Shutdown)`. | `Core.Tests: EstablishmentTests.AStoppingNodeSaysByeShutdownToItsPeers` |
| Two in-memory nodes connect through the same TLS, protocol, admission and drain as on sockets; a restarted node comes back with what it kept; a partition cuts and a heal rejoins. | `Testing.Tests: TestMeshTests.TwoNodesWhoseUsersKnowEachOtherConnect_OneSessionEach`, `Testing.Tests: TestMeshTests.AStoppedNodeIsGone_AndARestartedOneComesBackWithItsIdentityAndReconnects`, `Testing.Tests: TestMeshTests.APartitionCutsTheSession_AndHealingReconnects` |
| A request that needs the user key and does not find it on this device is `UserKeyAbsent` and changes nothing. | `Core.Tests: UserRequestTests.RemovingThisDeviceIsInvalid_AnotherUsersIsNotFound_WithoutTheUserKeyItIsUserKeyAbsent`, `Core.Tests: PackageRequestTests.AnEmptyNameIsInvalid_WithoutTheUserKeyItIsUserKeyAbsent` |
| No local request stores an object that would put the pinned bytes over the budget. | `Core.Tests: StoreRequestTests.ABudgetBelowThePinnedBytesMakesALocalWriteStoreFull_Budget0IsTheDefault` |
| A service writes only the kinds its configuration lists, and never a kind another connected service holds. | `Core.Tests: ServiceRoleTests.AKindOutsideTheConfigurationIsNotPermitted_OneAnotherServiceHoldsIsKindTaken` |
| The Host parses no payload: a payload that parses as nothing, of any kind, is stored and ordered like any other, while an Entry with a malformed envelope is never written here and never stored from a peer. | `Core.Tests: ServiceRoleTests.AMalformedEnvelopeIsInvalidHereAndMalformedFromAPeer_AnUnparseablePayloadOfAnyKindIsStoredAndOrdered`, `ReadmeSamples.Tests: ContentBlindHostTests.NoPayloadTypeIsReferencedByCoreOrHost` |
| A Fetch never waits for ever: without a possible source it fails within 60 seconds, naming what it lacks. | `Core.Tests: OperationKindTests.AFetchWithoutASourceWaits_AndFailsNotFoundAfter60Seconds` |
| A Fetch starts as soon as a peer that lists the Package is established, with nothing polling, and fetches only what a stored Entry of the Package reaches. | `Core.Tests: OperationKindTests.AFetchCompletesAsSoonAsASourceAppears_RecursivelyAndPinned`, `Core.Tests: OperationKindTests.AFetchOfAnObjectNoStoredEntryOfThePackageReferencesIsNotFoundAtOnce` |
| A Package nobody lists any more and nobody follows is forgotten with its Entries, unless something of it is pinned. | `Core.Tests: PackageRequestTests.AnUnfollowedPackageNoPeerListsIsDropped_AndSoIsAListedOneTheLastListerOmits` |
| Publishing a file pipelines its Chunks and still names them in file order, with memory bounded by the Chunks in flight; the first refusal in file order ends it. | `Core.Tests: StoreFilesTests.APipelinedPutStoresTheSameFileInFileOrderAsOneAtATime`, `Core.Tests: StoreFilesTests.APipelinedPutThatTheHostRefusesPartWayEndsWithTheRefusalAndPutsNoChunkList` |
| The client's helpers write a Tree to disk byte-equal and never leave a partial file visible, and never delete a directory they did not write. | `Core.Tests: StoreFilesTests.AFolderPublishedWithTheHelpersMaterialisesByteEqualFromGetObject_AndExistingIsHonoured`, `Core.Tests: StoreFilesTests.ACancelledMaterialiseLeavesNoPartialFileVisible` |
| An Entry of a kind no service knows replicates with every object its envelope references, and a redaction is honoured by its envelope whatever its kind. | `Core.Tests: ReplicationScenarioTests.AnEntryOfAKindNoServiceRegisteredReplicatesWithTheObjectsItsEnvelopeReferences`, `Core.Tests: RedactionTests.ARedactionIsReadFromTheEnvelope_AnyKindWithRedactsWithdraws_APayloadAloneDoesNot` |
| The command line, with the token the daemon wrote for it, writes Entries as the `cli` service end to end. | `Host.Tests: CliServiceTests.TheCliExternalServiceWithTheTokenFromConfigJsonRegistersWritesAnEntryAndPutsAFile` |

## Services

| Promise | Proved by |
|---|---|
| A spawned service gets a fresh token for every start; it admits that child's connections while the child runs, is withdrawn when the child exits, and appears in no log line or argument. | `Host.Tests: ServiceSpawnTests.EveryStartGetsAFreshTokenValidWhileItsChildRuns_WithThePipeHomeAndStateDirectoryInItsEnvironment`, `Core.Tests: LocalHelloTests.ASpawnedNameIsRefusedUntilTheHostSetsItsToken_AdmittedWithIt_AndRefusedAgainOnceCleared`, `Core.Tests: SpawnedTokenTests.AClearedTokenAdmitsNoHello_AllZeroIncluded` |
| A service the Host spawned is started again after it exits, with backoff; five crashes within five minutes or a configuration error give it up until the Host restarts, never silently; a child that exits on the Host's drain is not restarted, one that stays is killed after the grace. | `Host.Tests: SupervisionRuleTests.CrashesAreRestartedWithTheBackoffOfSection2Point3`, `Host.Tests: SupervisionRuleTests.FiveCrashesWithinFiveMinutesGiveUp_OlderOnesDoNotCount`, `Host.Tests: ServiceSpawnTests.TheEchoServiceSpawnedUnderDotnetConnectsAnswersACall_AndIsRestartedAfterItWasKilled`, `Host.Tests: SupervisorStopOrderTests.AChildThatExitsOnTheDrainIsStoppedNotRestarted`, `Host.Tests: ServiceSpawnTests.AChildThatStaysAfterTheDrainIsKilledOnceTheGraceIsOver` |
| A call to a service that is not connected fails at once; one it does not answer fails after 60 seconds, and its late reply changes nothing. | `Core.Tests: ServiceCallTests.AServiceThatIsNotConfiguredOrNotConnectedIsUnavailableAtOnce`, `Core.Tests: ServiceCallTests.AServiceThatDoesNotReplyWithin60SecondsTimesOut_AndItsLateReplyChangesNothing` |
| A slow service never holds up its caller's other requests, and a reply it cannot pass on closes the service's connection, not the caller's. | `Core.Tests: ServiceCallTests.ACallWaitingOnASlowServiceDoesNotHoldUpTheCallersOtherRequests`, `Core.Tests: ServiceCallTests.AMalformedReplyClosesTheServicesConnectionAsAViolation_AndItsCallerHearsUnavailable` |
| A service's call handler never blocks the reading of its connection, and every call is answered once. | `Local.Tests: CallbackServingTests.AHandlersOwnRequestIsAnsweredWhileItRuns`, `Local.Tests: CallbackReplyOnceTests.ACallbackIdThatArrivesTwiceIsAnsweredOnce` |
| Every Entry of a service's kinds reaches it once, in store order, across reconnects and restarts; a handler that failed gets its Entry again. | `Service.Tests: CatchUpTests.AServiceAwayWhile300EntriesWereStoredReceivesEachOnceInOrder`, `Service.Tests: CatchUpTests.AServiceStartedAgainResumesAfterItsCursor_WithoutRepeats`, `Service.Tests: CursorReplayTests.AnEntryWhoseHandlerFailedIsHandedOverAgainOnTheNextRun`, `Testing.Tests: MeshScenarios.AServiceRunningBesideANodeReceivesEveryEntryOfItsKindsOnceInStoreOrder_AcrossAReconnect_AndAnswersCalls` |

## Axiom.Mesh.Chat

| Promise | Proved by |
|---|---|
| Two devices holding the same messages of a Channel show them in the same order, offline writes included. | `Chat.Tests: ChatScenarios.BothSendWithAttachments_AndBothShowTheSameKeysInTheSameOrder`, `Chat.Tests: ChatScenarios.MessagesWrittenWhileTheOtherWasOfflineSlotInByClock_IdenticallyOnBoth`, `Chat.Tests: ChatScenarios.ChatShowJsonIsEqualOnBothDevices` |
| A withdrawn message is never shown, whichever of it and its Redaction arrived first; a withdrawal the specification would not honour is refused before anything is written. | `Chat.Tests: ChatScenarios.ARedactionStoredBeforeItsTargetIsAppliedWhenTheTargetArrives`, `Chat.Tests: ChatScenarios.BobWithdrawsHisOwnMessage_BothShowItWithdrawn_NeverTheText`, `Chat.Tests: ChatScenarios.TheOwnerWithdrawsBobsMessage_AndBobCannotWithdrawTheOwners` |
| An attachment fetched with `chat get` is byte-equal to what was attached, and never partial under its name. | `Chat.Tests: ChatScenarios.AnAttachedFolderAndFileMaterialiseByteEqualOnTheOtherDevice`, `Core.Tests: StoreFilesTests.ACancelledFileMaterialiseLeavesNeitherTheFileNorAPartialOne` |
| A restarted service catches up by store sequence without a duplicate, and an index of another store epoch is discarded. | `Chat.Tests: ChatScenarios.AServiceRestartCatchesUpByStoreSequenceWithoutDuplicates_AcrossAGapOver256`, `Chat.Tests: ChatScenarios.AnotherStoreEpochDiscardsTheIndex_AndItIsRebuiltFromTheHostItServesNow` |
| A killed chat service is started again by its Host, and the command line reaches it again. | `Integration.Tests: ChatProcessTests.TheDaemonSpawnsTheChatService_RestartsItWhenKilled_AndChatListWorksThroughout` |
| A 1 MiB attachment goes from `chat send` to `chat get` within 10 s, and 10 000 messages are caught up within 10 s (Release, `AXIOM_MESH_FLOORS=1`). | `Integration.Tests: ChatFloorTests.AOneMebibyteAttachmentGoesFromChatSendToChatGetWithinTenSeconds`, `Integration.Tests: ChatFloorTests.TenThousandMessagesAreCaughtUpWithinTenSeconds` |

## Axiom.Mesh.Sync

| Promise | Proved by |
|---|---|
| A folder bound on two devices holds the same files, byte-equal, once both ran; an empty folder bound to a branch receives it. | `Sync.Tests: SyncScenarios.AlicesFolderAppearsByteEqualInBobsEmptyBoundDirectory`, `Sync.Tests: SyncScenarios.AnEditOnBobReachesAlice_OneLeafOnBoth`, `Sync.Tests: SyncScenarios.ManySmallFilesAndALargeOneArriveByteEqual` |
| Edits made while apart merge into one Version naming both leaves; the same leaves merge to the same files on every device. | `Sync.Tests: SyncScenarios.EditsToDifferentFilesWhileApartMergeWithoutConflict_OneMergeVersionWithTwoParents`, `Sync.Tests: SyncScenarios.ThreeConcurrentLeavesResolveByAChainOfTwoParentMerges_IdenticallyEverywhere`, `Sync.Tests: MergeTableTests.TheMergeIsTheSameWhicheverLeafIsCalledOurs` |
| A path changed differently on two devices loses nothing: the later leaf's file stands and the other side's file, or whole subtree, lies beside it under a conflict name, on both devices. | `Sync.Tests: SyncScenarios.TheSameFileEditedOnBoth_TheLaterLeafWins_TheOtherLiesBeside_IdenticallyOnBoth`, `Sync.Tests: MergeTableTests.ADirectoryReplacedByAFileIsChangedDifferently_TheOtherSidesSubtreeLiesBeside` |
| A run stopped at any point never writes a second Version for the same Tree nor leaves a partial file under a name, and the next run writes only what is left. | `Sync.Tests: CrashResumeTests.AStopRightAfterPublishingNeverMakesASecondVersion`, `Sync.Tests: CrashResumeTests.AResumedPullWritesOnlyWhatIsLeft_NeverAFileChangedMeanwhile_NoPartialFile`, `Sync.Tests: CrashResumeTests.AStopAfterTheMergeWasWrittenWritesNoSecondMerge`, `Sync.Tests: SyncScenarios.AServiceStoppedMidSyncResumes_NoDuplicateVersionNoPartialFile` |
| A pull never overwrites or deletes a file changed in the folder since the last run; a read-only binding never publishes, and says how many local changes it keeps. | `Sync.Tests: DiskDiffTests.AFileChangedHereSinceTheBaseIsNeverOverwrittenOrDeletedByAPull`, `Sync.Tests: SyncScenarios.AReadOnlyFollowerPullsAndNeverPublishes_ItsEditsAreReported` |
| Unbinding leaves every file; binding again, or another store epoch, merges what the folder holds with the branch, losing no local edit. | `Sync.Tests: SyncScenarios.UnbindLeavesTheFiles_RebindingMergesThemAsLocalChanges`, `Sync.Tests: SyncScenarios.AnotherStoreEpochMakesTheBindingAFirstSync_NoLocalEditLost` |
| A device that was away or stopped catches up when it runs again, and writes no Version when nothing changed. | `Sync.Tests: SyncScenarios.AServiceThatStoppedCatchesUpWhenItRunsAgain`, `Sync.Tests: SyncScenarios.ANodeOfflineForAWhileCatchesUp_NoVersionWrittenWhenNothingChanged` |
| The Host spawns the sync service and a bound folder is published through it; `host install` adds it to an older configuration unless told not to. | `Host.Tests: SyncServiceTests.TheSyncServiceSpawnedByTheDaemonRegistersArchives_AndPublishesABoundFolder`, `Cli.Tests: HostInstallTests.InstallAddsTheSyncServiceToAnOlderConfiguration_UnlessNoSync` |
| A changed 1 MiB file is a Version within a second of the debounce, an unchanged folder of 100 000 files is scanned within 2 s, and a folder of 10 000 files and 64 MiB reaches a second node within two minutes (Release, `AXIOM_MESH_FLOORS=1`). | `Integration.Tests: SyncFloorTests.AChangedMebibyteFileIsAVersionWithinASecondOfTheDebounce`, `Integration.Tests: SyncFloorTests.AnUnchangedFolderOfAHundredThousandFilesIsScannedWithinTwoSeconds`, `Integration.Tests: SyncFloorTests.TenThousandFilesAndSixtyFourMebibytesArriveWithinTwoMinutes` |

## Axiom.Mesh.Cli

| Promise | Proved by |
|---|---|
| Every failure of the command line is one line and an exit code (1 usage, 2 Host not reachable, 3 refused, 4 timeout), never a stack trace. | `Cli.Tests: FailureTests.AHostThatIsNotRunningIsExplainedNotStackTraced`, `Cli.Tests: FailureTests.EveryStatusCodeOfLocalSpec11HasAnExitCodeAndAOneLineExplanation`, `Cli.Tests: CommandLineTests.AMissingArgumentOrAWrongValueIsAUsageErrorThatExits1` |
| A withdrawn message is never shown with its text, in a table, in JSON or in `watch`. | `Cli.Tests: EntryDisplayTests.ARedactedMessageIsShownAsWithdrawnNeverItsText` |
| Every command has help text, and a prefix that names two ids is refused naming both, never guessed. | `Cli.Tests: HelpTextTests.EveryCommandHasHelpText`, `Cli.Tests: IdMatchTests.AnAmbiguousPrefixNamesTheCandidates_AnUnknownOneSaysNoneMatches` |
| A folder published from the command line materialises byte-equal on another device. | `Cli.Tests: CliScenarioTests.APublishedFolderMaterialisesByteEqualOnASecondNodeThatFollowsIt` |

## Install and release

| Promise | Proved by |
|---|---|
| The Host starts at the user's logon as the release design says: on Windows a per-user logon task with the user's own token, no time limit, not stopped on batteries, running the daemon without a window; on Linux a systemd user unit enabled for the default target. An uninstall stops the Host and removes exactly that registration. | `Cli.Tests: AutostartPlanTests.OnWindowsTheHostIsAPerUserLogonTaskRunningTheDaemonHiddenWithoutATimeLimit`, `Cli.Tests: AutostartPlanTests.OnLinuxTheHostIsASystemdUserUnitEnabledForTheDefaultTarget`, `Cli.Tests: AutostartPlanTests.AnUninstallStopsTheHostAndRemovesTheRegistration_WhatIsNotThereIsNoFailure` |
| Registering the Host never asks the Host, and a system program that fails is one line and exit 3. | `Cli.Tests: HostInstallTests.InstallRegistersAndStartsTheHostWithoutAskingIt`, `Cli.Tests: HostInstallTests.ASystemProgramThatFailsIsOneLineAndExit3` |
| A path in the registration is taken literally by the system that runs it, spaces, `%` and `$` included. | `Cli.Tests: AutostartPlanTests.AHomeOfItsOwnIsPassedAsHome_QuotedSoThatEveryPathIsTakenLiterally` |
| The command line finds a Host whose `config.json` names its own pipe, by the precedence the Host listens by. | `Local.Tests: PipeLocationTests.TheVariableWinsOverTheConfiguredName_WhichWinsOverTheDefault`, `Cli.Tests: PipeNameTests.PipeNameIsReadFromConfigJson` |
| A store written by a build before 0.1.0 is refused at start, with the steps to replace it; nothing in it is changed. | `Core.Tests: StoreSequenceTests.AStoreOfAPreReleaseBuildIsRefusedAtStart_AndTheStepsSayToDeleteIt` |
| A dropped Package is announced to every subscriber of its topic. | `Core.Tests: PackageRequestTests.AnUnfollowedPackageNoPeerListsIsDropped_AndSoIsAListedOneTheLastListerOmits` |
| Where the platform needs it, a stored object's directory is flushed after the rename, so a complete object survives a power failure. | `Core.Tests: StoreTests.TheStoreDirectoryIsFlushedWhereThePlatformNeedsIt_AndNeedsNoFlushOnWindows` |
| The local protocol's published vectors decode and re-encode to the same bytes, so another implementation can check itself against them. | `Local.Tests: VectorTests.EveryPublishedVectorDecodesAndReencodesToTheSameBytes` |

The install scripts, the release workflow and the install job are not unit-tested; what they do is described in
[ACCEPTANCE.md](ACCEPTANCE.md) and `.github/workflows/install-tests.yml`, and the checksum check is the install script's
own (it refuses a zip that does not match `SHA256SUMS`).

## Performance

The hot paths keep their allocation budgets in Release builds (`[ReleaseFact]`, deterministic: the least of several
runs); throughput is measured by the benchmark suite and recorded with its command in
[BENCHMARKS.md](BENCHMARKS.md), which promises nothing a test does not hold. The release floors run with
`AXIOM_MESH_FLOORS=1`.

| Promise | Proved by |
|---|---|
| Hashing a mebibyte and comparing ids allocate nothing. | `Model.Tests: AllocationTests.HashingAMebibyteAndComparingIdsAllocatesNothing` |
| Cutting a file into Chunks allocates nothing per Chunk: each is sealed in a reused buffer. | `Model.Tests: AllocationTests.SealingAChunkInAReusedBufferAllocatesNothing` |
| Parsing a ChunkList of 32 768 Chunks allocates only the parsed object; verifying an Entry nothing that grows with it. | `Model.Tests: AllocationTests.ParsingAChunkListAllocatesOnlyTheParsedObject`, `Model.Tests: AllocationTests.VerifyingAnEntryAllocatesNothingThatGrowsWithTheEntry` |
| Decoding a 1 MiB `Object` frame allocates only the message: the object is read in place. | `Protocol.Tests: AllocationTests.DecodingA1MiBObjectFrameAllocatesOnlyTheMessage` |
| Admitting a 1 MiB Chunk from a peer allocates only its pooled copy; forwarding one makes no further whole copy. | `Core.Tests: AllocationTests.AdmittingAMebibyteChunkAllocatesOnlyThePooledCopy`, `Core.Tests: ReplicationRegressionTests.ForwardingAMebibyteObjectAllocatesNoFurtherWholeCopy` |
| A `GetIdentity` round trip allocates only its messages on the client. | `Local.Tests: AllocationTests.AGetIdentityRoundTripOnTheClientAllocatesOnlyTheMessages` |
| 1 GiB of Chunks crosses loopback with real TLS within 60 seconds, and a history of 10 000 Entries is walked within two minutes (Release, `AXIOM_MESH_FLOORS=1`). | `Integration.Tests: FloorTests.OneGibibyteOfChunksCrossesLoopbackWithRealTlsWithinSixtySeconds`, `Integration.Tests: FloorTests.AHistoryOfTenThousandEntriesIsWalkedWithinTwoMinutes` |
