# Guarantees

What Axiom.Mesh promises, each with the test that proves it. **A promise without a test is not made:** a row whose
test does not exist yet says `planned:` before the test that will prove it, and is a goal, not a promise. Test names
are `<test project>: <class>.<method>`; projects live under `UnitTests/` (or `IntegrationTests/`), `Shared` is
`UnitTests/Shared`, whose classes every suite derives. `GuaranteeTests` (in `ReadmeSamples.Tests`) fails when a row
names no test, or when a proved row names a test that does not exist.

## Trust and peers

| Promise | Proved by |
|---|---|
| A peer whose key is in no current roster is closed before a protocol byte is sent. | planned: `Core.Tests: HandshakeTests.APeerWhoseKeyIsInNoRosterIsClosedWithoutAProtocolByte` |
| A removed device stays removed in every later roster of its user, whatever that roster says; an omitted device counts as removed. | `Model.Tests: RosterTests.RemovalIsPermanent_AnOmittedDeviceCountsAsRemoved_ADeviceIdInTwoUsersRostersIsRemovedInTheLater` |
| A removed device is closed and refused for ever at the handshake. | planned: `Core.Tests: HandshakeTests.ARemovedDeviceIsRefusedAtTheHandshake` |
| No object becomes storable without its hash, its form and its signer checked: a signed object outside Model cannot be made storable without verification, and one flipped bit makes it invalid. | `Model.Tests: StorableObjectSurfaceTests.NoStorableObjectCanBeMadeOutsideModelWithoutVerification`, `Model.Tests: SignedObjectTests.OneFlippedBitInTheBodyMakesAnEntryManifestRosterOrGrantInvalidAndRenamesIt` |
| An Entry is stored only from a device of the owner or of a current write grantee, and stays valid once stored. | `Model.Tests: AdmissionTests.AnEntryIsValidOnlyFromADeviceOfTheOwnerOrAWriteGrantee_AndStaysValidAfterItsDeviceIsRemoved` |
| Nothing unverified is stored, served or forwarded: an object is checked against its hash, its form and its signer before anything acts on it. | planned: `Core.Tests: IntakeTests.ObjectsApplyInTheOrderRosterManifestGrantEntryPayloadWhateverTheirArrival_Bounded8MiBPerSession64MiBInAll` |
| A Package is listed, claimed and served only to its owner and its current grantees. | planned: `Core.Tests: VisibilityTests.TheListingHoldsExactlyThePackagesWhoseOwnerOrCurrentGranteeIsThePeersUser`, `Core.Tests: VisibilityTests.AChunkIsServedThroughAnyVisiblePackageThatReferencesIt_ElseNotFound` |
| A revocation reaches the revoked user, who is then no longer listed or served. | planned: `Core.Tests: VisibilityTests.ARevokedUserIsNoLongerListedOrServed_ButReceivesTheRevocationGrant` |
| Two nodes holding two sessions to each other decide which one to keep identically, without talking. | `Model.Tests: DuplicateSessionTests.Decide` |
| One session per device id: two nodes that connect twice keep the same one without talking. | planned: `Testing.Tests: MeshScenarios.DuplicateConnectionsSettleOnOneSessionPerDevice` |

## Bounds

| Promise | Proved by |
|---|---|
| Every bound of an AMP/1 object is enforced: one over makes the object malformed, at the bound it is accepted. | `Model.Tests: ObjectBoundsTests.EveryObjectBoundIsAcceptedAtTheLimitAndMalformedOneOver` |
| Every bound of AMP/1 is enforced: one over is refused, at the bound is accepted, as a violation in a message and as malformed in an object. | planned: `Protocol.Tests: LimitTests.EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit_AViolationInAMessageMalformedInAnObject` |
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
| A change crosses any number of hops without polling, and is never sent back to where it came from. | planned: `Testing.Tests: MeshScenarios.AChangeCrossesThreeNodesWithoutPolling_AndNothingIsSentBack` |
| The same scenario passes in memory and on sockets. | planned: `Integration.Tests: RealMeshScenarios.EveryInMemoryScenarioPassesOnSocketsDiskAndTheRealPipe` |

## The packages

| Promise | Proved by |
|---|---|
| A package references only the layers below it; no service and no app reaches the engine or the daemon. | `Shared: ConventionTests.ReferencesOnlyTheStatedFamilyPackages`, `Shared: ServiceOrAppConventionTests.NoServiceOrAppReferencesCoreOrHost` |
| Every failure a package throws says who must act, whether retrying can succeed, and at least one step out. | `Shared: ExceptionRecoveryContractTests.EveryExceptionCarriesTheFullRecoveryContract`, `Model.Tests: FailureGuidanceTests.AFailureCannotBeMadeWithoutAMessageOrWithABlankStep` |
| A compiled consumer keeps working when a minor version adds members. | `Shared: PublicSurfaceTests.NoPublicMemberTakesAnOptionalParameter`, `Shared: PublicSurfaceTests.NoPublicPositionalRecordWithoutAClosedShape` |
| No package holds state shared by every node of a process. | `Shared: ConventionTests.HoldsNoMutableStaticState` |

## Axiom.Mesh.Local

| Promise | Proved by |
|---|---|
| Every bound a frame of the local protocol carries is enforced where the bytes enter: one over is a violation from a client and a lost connection from the Host, at the bound it reads. | `Local.Tests: LimitTests.EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit`, `Local.Tests: BoundsTests.AFieldOutsideItsBoundsInARequestIsAViolation`, `Local.Tests: BoundsTests.AFieldOutsideItsBoundsInAResponseOrAnEventIsAHostDefect` |
| An id one side does not know never closes the connection: an unknown request is answered `UnknownRequest`, an unknown event discarded, an unknown callback answered. | `Local.Tests: UnknownIdTests.AnUnknownRequestIdIsAnsweredUnknownRequestAndTheConnectionStaysOpen`, `Local.Tests: UnknownIdTests.AnUnknownEventIsDiscardedByTheClient_TheNextOneArrives`, `Local.Tests: UnknownIdTests.AnUnknownCallbackIsAnsweredUnknownRequestByTheService` |
| A value a newer Host sends that this client does not know is kept, never a failure. | `Local.Tests: EnumToleranceTests.AnUnknownEnumeratedValueInAnEventIsReadAndKept` |
| A request the Host refuses is a status and an error in the client, never an exception. | `Local.Tests: LocalClientExchangeTests.ARequestIsAnsweredWithItsOkBodyAndARefusalIsAValue` |
| The client never sends a 65th outstanding request, and a reader that falls behind loses only its own subscription. | `Local.Tests: ClientLimitTests.AClientRefusesA65thOutstandingRequestBeforeSendingIt`, `Local.Tests: ClientLimitTests.AReaderThatFallsBehindLapsesOnlyItsOwnSubscription_OthersAndRequestsContinue` |
| Nothing of the engine or the daemon is reachable from the package every app and service references. | `Local.Tests: SurfaceTests.NoCoreTypeIsReachableFromLocalsPublicSurface` |
