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
| A removed device is closed and refused for ever, whatever a later roster says. | planned: `Core.Tests: HandshakeTests.ARemovedDeviceIsRefusedAtTheHandshake`, `Model.Tests: RosterTests.RemovalIsPermanent_AnOmittedDeviceCountsAsRemoved_ADeviceIdInTwoUsersRostersIsRemovedInTheLater` |
| Nothing unverified is stored, served or forwarded: an object is checked against its hash, its form and its signer before anything acts on it. | planned: `Core.Tests: IntakeTests.ObjectsApplyInTheOrderRosterManifestGrantEntryPayloadWhateverTheirArrival_Bounded8MiBPerSession64MiBInAll`, `Model.Tests: SignedObjectTests.OneFlippedBitInTheBodyMakesAnEntryManifestRosterOrGrantInvalidAndRenamesIt` |
| A Package is listed, claimed and served only to its owner and its current grantees. | planned: `Core.Tests: VisibilityTests.TheListingHoldsExactlyThePackagesWhoseOwnerOrCurrentGranteeIsThePeersUser`, `Core.Tests: VisibilityTests.AChunkIsServedThroughAnyVisiblePackageThatReferencesIt_ElseNotFound` |
| A revocation reaches the revoked user, who is then no longer listed or served. | planned: `Core.Tests: VisibilityTests.ARevokedUserIsNoLongerListedOrServed_ButReceivesTheRevocationGrant` |
| One session per device id: two nodes that connect twice keep the same one without talking. | planned: `Model.Tests: DuplicateSessionTests.Decide`, `Testing.Tests: MeshScenarios.DuplicateConnectionsSettleOnOneSessionPerDevice` |

## Bounds

| Promise | Proved by |
|---|---|
| Every bound of AMP/1 is enforced: one over is refused, at the bound is accepted, as a violation in a message and as malformed in an object. | planned: `Protocol.Tests: LimitTests.EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit_AViolationInAMessageMalformedInAnObject` |
| Every bound of the local protocol is enforced, one over refused and at the bound accepted. | planned: `Local.Tests: LimitTests.EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit` |
| A slow local client loses its subscription, never the mesh: other subscriptions and requests continue. | planned: `Core.Tests: SubscriptionTests.A257thEventOr16MiBLapsesOnlyThatSubscription_OthersAndRequestsContinue_64MiBUnacceptedIsByeUnresponsive` |

## The pipe

| Promise | Proved by |
|---|---|
| The pipe admits the Host's operating-system user only. | planned: `Host.Tests: PipeAdmissionTests.Decide`, `Integration.Tests: PipeTests.AnotherUserIsRefusedByTheAccessListAndByProcessIdentity` |
| No record of the local protocol can carry a private key. | planned: `Local.Tests: NoKeyCrossesThePipeTests.NoRecordHasAFieldThatCanHoldAPrivateKey` |

## Replication

| Promise | Proved by |
|---|---|
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
