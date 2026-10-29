# Guarantees

What Axiom.Mesh promises, each with the test that proves it. **A promise without a test is not made:** a row whose
test does not exist yet says `planned:` before the test that will prove it, and is a goal, not a promise. Test names
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

## Bounds

| Promise | Proved by |
|---|---|
| Every bound of an AMP/1 object is enforced: one over makes the object malformed, at the bound it is accepted. | `Model.Tests: ObjectBoundsTests.EveryObjectBoundIsAcceptedAtTheLimitAndMalformedOneOver` |
| Every bound of AMP/1 is enforced: one over is refused, at the bound is accepted, as a violation in a message and as malformed in an object. | `Protocol.Tests: LimitTests.EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit_AViolationInAMessageMalformedInAnObject` |
| Every bound is a named constant with the specification's value. | `Model.Tests: LimitsDocumentTests.EveryLimitOfTheSpecIsANamedConstantWithTheSpecsValue`, `Model.Tests: LimitsTests.EveryLimitHasTheValueItsSectionStates` |
| Every bound of the local protocol is enforced, one over refused and at the bound accepted. | planned: `Local.Tests: LimitTests.EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit` |
| A slow local client loses its subscription, never the mesh: other subscriptions and requests continue. | planned: `Core.Tests: SubscriptionTests.A257thEventOr16MiBLapsesOnlyThatSubscription_OthersAndRequestsContinue_64MiBUnacceptedIsByeUnresponsive` |

## The pipe

| Promise | Proved by |
|---|---|
| The pipe admits the Host's operating-system user only. | planned: `Host.Tests: PipeAdmissionTests.Decide`, `Integration.Tests: PipeTests.AnotherUserIsRefusedByTheAccessListAndByProcessIdentity` |
| No record of the local protocol can carry a private key. | `Local.Tests: NoKeyCrossesThePipeTests.NoRecordHasAFieldThatCanHoldAPrivateKey` |

## Replication

| Promise | Proved by |
|---|---|
| Every node that holds the same Entries orders them identically. | `Model.Tests: EntryOrderTests.EntriesOrderByClockThenDeviceIdThenHash_IdenticallyOnEveryNode` |
| The parents and clock chosen for a new Entry always make it well-formed and faultless, or writing stops with `ClockExhausted`. | `Model.Tests: ParentChoiceTests.Decide` |
| A `WriteEntry` with no parents produces an Entry that orders identically on every node. | planned: `Core.Tests: WriteEntryTests.WithNoParentsTheEntryVerifiesOrdersAndIsFaultless_ForTheFourParentCases_AndTwoParentsWhenTheHighestClockIs2Pow32Plus1AboveTheOwnHead` |
| A change crosses any number of hops without polling, and is never sent back to where it came from. | `Core.Tests: ReplicationScenarioTests.AChangeCrossesTwoHopsWithoutPolling`, `Core.Tests: ReplicationScenarioTests.AThirdNodeRelaysAndNothingIsSentBackToTheSource` |
| The same scenario passes in memory and on sockets. | planned: `Integration.Tests: RealMeshScenarios.EveryInMemoryScenarioPassesOnSocketsDiskAndTheRealPipe` |

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
| A peer that closes before its `Hello` is a refusal: the node holds its next dial back 10 seconds, doubling to 5 minutes, and redials on its own. | `Core.Tests: EstablishmentTests.ACloseAfterTheHandshakeBeforeHelloCountsAsARefusalAndBacksOff`, `Core.Tests: BackoffTests.AFailedDialWaits10SecondsThenDoublesToAtMost5Minutes` |
| A peer's `Hello` that names another device or another user is answered `Bye(Unauthorized)` and counted against the device. | `Core.Tests: HelloTests.ADeviceIdThatIsNotTheHashOfTheTlsKeyOrNotInTheUsersCurrentRosterIsByeUnauthorized` |
| A node admits at most 4 connections per peer address. | `Core.Tests: AdmissionTests.AdmissionPerAddressIs4` |
| Two nodes with nothing to say keep their session on heartbeats alone. | `Core.Tests: LivenessTests.AnEstablishedSessionSurvivesFiveMinutesOfSilenceWithHeartbeats` |
| A stopping node ends every session with `Bye(Shutdown)`. | `Core.Tests: EstablishmentTests.AStoppingNodeSaysByeShutdownToItsPeers` |
| Two in-memory nodes connect through the same TLS, protocol, admission and drain as on sockets; a restarted node comes back with what it kept; a partition cuts and a heal rejoins. | `Testing.Tests: TestMeshTests.TwoNodesWhoseUsersKnowEachOtherConnect_OneSessionEach`, `Testing.Tests: TestMeshTests.AStoppedNodeIsGone_AndARestartedOneComesBackWithItsIdentityAndReconnects`, `Testing.Tests: TestMeshTests.APartitionCutsTheSession_AndHealingReconnects` |
