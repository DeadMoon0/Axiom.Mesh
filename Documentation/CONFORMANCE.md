# Conformance

Maps every requirement of [SPEC.md](SPEC.md) (AMP/1) and [LOCAL-SPEC.md](LOCAL-SPEC.md) (`axiom-mesh-local/1`) that
the reference implementation proves to the test that proves it. The seed rows are the conformance lists of SPEC.md §15
and LOCAL-SPEC.md §15; each milestone appends the MUSTs of the sections it implements (SPEC.md §4–§11, LOCAL-SPEC.md
§3–§9) under the heading of its own project.

Format: one table per heading, with the columns **Section** (`SPEC §n.n` or `LOCAL §n.n`, several separated by commas;
a bare `§` belongs to the document named before it), **Requirement** (what the specification demands, in a sentence),
**Behaviour** (what the implementation does to meet it) and **Test** (`` `<test project>: <class>.<method>` ``, or
`planned`, optionally followed by the test that will prove it). `ConformanceDocTests` (in `ReadmeSamples.Tests`) fails
when a row cites a section that does not exist, or names a test that does not exist without saying `planned`.

## AMP/1 (SPEC.md §15)

| Section | Requirement | Behaviour | Test |
|---|---|---|---|
| SPEC §5, §7 | Every message is encoded and decoded at every byte split. | The AMP/1 definition round-trips each message through ProtoStream's harness at every split. | planned: `Protocol.Tests: MessageRoundTripTests.EveryMessageRoundTripsAtEveryByteSplit` |
| SPEC §5.2, §12.3 | Every limit is refused one over and accepted at the limit; a violation in a message, malformed in an object. | Every list and string reader checks the count against its maximum before reading content. | planned: `Protocol.Tests: LimitTests.EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit_AViolationInAMessageMalformedInAnObject` |
| SPEC §8.1, §2.2 | A signed object verifies, and is invalid after one bit of its body is flipped, which also renames it. | Signed objects verify over H(domain ‖ body) of the bytes received. | planned: `Model.Tests: SignedObjectTests.OneFlippedBitInTheBodyMakesAnEntryManifestRosterOrGrantInvalidAndRenamesIt` |
| SPEC §2.2 | A signature whose s is above (n−1)/2 is malformed; signers produce low-s. | Signatures are parsed with the low-s bound and normalised when produced. | planned: `Model.Tests: SignatureTests.ASignatureWhoseSIsAboveHalfTheOrderIsMalformed_AndSignersAlwaysProduceLowS` |
| SPEC §8.7, §2.3 | A Package id is H(domain ‖ owner id ‖ nonce); a Manifest whose id does not derive is malformed. | The Manifest parser re-derives the id from its owner key and nonce. | planned: `Model.Tests: PackageIdTests.APackageIdIsH_Domain_OwnerId_Nonce_AndAManifestWhoseIdDoesNotDeriveIsMalformed` |
| SPEC §9.3 | Entries order by clock, then device id, then hash, identically on every node. | The total order is one comparison over the three keys as unsigned byte strings. | planned: `Model.Tests: EntryOrderTests.EntriesOrderByClockThenDeviceIdThenHash_IdenticallyOnEveryNode` |
| SPEC §9.5 | The leaf rule and the merge table hold against fixed inputs, including a criss-cross merge. | Deferred to phase 1b (Sync); see STRUCTURAL-DEBT.md entry 5. | planned |
| SPEC §9.7 | Package objects are visible to the owner and current grantees only, with and without a Grant and after a revocation. | Listing, Have, Want answers and serving consult one visibility rule. | planned: `Core.Tests: VisibilityTests.TheListingHoldsExactlyThePackagesWhoseOwnerOrCurrentGranteeIsThePeersUser` |
| SPEC §9.8 | Rosters are served only to whom §9.8 allows. | Roster queries consult the same visibility rule. | planned: `Core.Tests: VisibilityTests.ARosterIsServedOnlyToItsOwnUserAndToUsersThatShareAPackage` |
| SPEC §7.3, §10.5 | A change is relayed across three nodes and never sent back to its origin. | The change bus fans out PackageChanged and Have to every session but the origin. | planned: `Testing.Tests: MeshScenarios.AChangeCrossesThreeNodesWithoutPolling_AndNothingIsSentBack` |
| SPEC §4.5 | The duplicate rule decides all three cases identically on both nodes. | A pure decision over idle time, dialler and device ids. | planned: `Model.Tests: DuplicateSessionTests.Decide` |
| SPEC §6.2, §4.4 | A peer that closes after the handshake counts as a refusal and is backed off. | The peer table records a close before Hello as a refusal. | planned: `Core.Tests: EstablishmentTests.APeerThatClosesAfterTheHandshakeCountsAsARefusalAndIsBackedOff` |
| SPEC §15 | Test vectors: encoded objects with their hashes and signatures under published test keys. | `Documentation/vectors/` is generated and verified. | planned: `Model.Tests: VectorTests.EveryPublishedVectorParsesHashesAndVerifies` |

## axiom-mesh-local/1 (LOCAL-SPEC.md §15)

| Section | Requirement | Behaviour | Test |
|---|---|---|---|
| LOCAL §3, §14.1 | Every message is encoded and decoded at every byte split. | Both sides' definitions round-trip each message through ProtoStream's harness. | planned: `Local.Tests: MessageRoundTripTests.EveryMessageRoundTripsAtEveryByteSplit` |
| LOCAL §12, §11.3 | Every limit is refused one over and accepted at the limit, with the violation-versus-error distinction. | Limits are enforced at the frame and the request, as violations or statuses as §11 says. | planned: `Local.Tests: LimitTests.EveryLimitIsRefusedOneOverAndAcceptedAtTheLimit` |
| LOCAL §6, §7, §8, §9, §10 | Every request is answered with its OK body and every listed error against a fixed Host state. | The dispatcher's table; requests outside the phase-1 set answer `UnknownRequest` (STRUCTURAL-DEBT.md entry 12). | planned |
| LOCAL §3.3 | The class rules hold for a frame of each class from each side. | The frame codec classifies by id range before parsing the body. | planned: `Local.Tests: FrameClassTests.AFrameOfEachClassFromEachSideIsAcceptedOrAViolationAsTheClassRulesSay` |
| LOCAL §4.2 | `Hello` is refused for each reason, `Duplicate` for a live service name, an in-process name claimed on the pipe, and a spawned or external name with a wrong token. | The Hello checks run in the order of §4.2; tokens compare in constant time. | planned: `Core.Tests: HelloTests.Refusals` |
| LOCAL §9.4, SPEC §8.6, §9.3 | A `WriteEntry` with no parents produces an Entry that verifies, orders and is faultless, for the four parent cases and the two-parent case. | Parents are chosen by the pure parent rule. | planned: `Core.Tests: WriteEntryTests.WithNoParentsTheEntryVerifiesOrdersAndIsFaultless_ForTheFourParentCases_AndTwoParentsWhenTheHighestClockIs2Pow32Plus1AboveTheOwnHead` |
| LOCAL §9.4, SPEC §8.5 | A Message, Version or Redaction payload that does not parse is `Invalid` here, and the same Entry from a peer is dropped as faulty. | The Host parses the built-in payload layouts itself. | planned: `Core.Tests: WriteEntryTests.AVersionMessageOrRedactionPayloadThatDoesNotParseIsInvalidHere_AndDroppedAsFaultyFromAPeer` |
| LOCAL §5.6, SPEC §9.4 | A Redaction sets `Entry.redacted` on its target. | Redactions are applied by the Host on store. | planned: `Core.Tests: RedactionTests.AnHonouredRedactionMarksItsTargetRedacted` |
| LOCAL §7.2, §7.4 | Replay delivers exactly the Entries with `storeSeq` ≥ `fromSeq`, once and in order, before and across a lapse. | Subscriptions replay from the index, then go live. | planned: `Core.Tests: SubscriptionTests.FromSeqDeliversExactlyTheEntriesWithStoreSeqAtLeastFromSeqOnceInOrderThenLive_Over256IsGapTooLarge` |
| LOCAL §9.3 | A client discards its index on a changed `storeEpoch`. | The client library compares the epoch of `HelloAck` with the one it indexed under. | planned: `Local.Tests: StoreEpochTests.AClientDiscardsItsIndexWhenHelloAckStoreEpochDiffersFromTheOneItIndexedUnder` |
| LOCAL §7.4 | The slow-consumer rule leaves other subscriptions and requests intact. | A full subscription lapses alone. | planned: `Core.Tests: SubscriptionTests.A257thEventOr16MiBLapsesOnlyThatSubscription_OthersAndRequestsContinue_64MiBUnacceptedIsByeUnresponsive` |
| LOCAL §8.5 | An operation is cancelled by `CancelOperation`, by `Drain` and by a closed connection, with status `Cancelled`. | Each operation carries its own cancellation, linked to the connection's. | planned: `Core.Tests: OperationTests.AnOperationIsCancelledByCancelOperationByDrainAndByAClosedConnection` |
| LOCAL §9.4, §14.8 | An `Outbound` pin is released after the object was sent once. | The pin is released when a peer fetched the object. | planned: `Core.Tests: PinTests.EveryObjectAnEntryWrittenHereReferencesIsPinnedOutboundUntilSentOnce` |
| LOCAL §9.7 | A direct read keeps an object's bytes while it is evicted. | Store files are opened with delete sharing; eviction unlinks. | planned: `Core.Tests: StoreTests.AnObjectOpenedFromTheStoreDirectoryKeepsItsBytesWhileEvicted_AndTheHostCountsItEvictedOnlyAfterUnlink` |
| LOCAL §2.2 | The pipe admits the Host's OS user and refuses another, by access list and by process identity, on each desktop platform. | The pipe listeners check the peer's process identity before `Hello` is answered. | planned: `Host.Tests: PipeAdmissionTests.Decide` |
| LOCAL §15 | Test vectors of the local protocol. | Published with the reference implementation. | planned: `Local.Tests: VectorTests.EveryPublishedVectorDecodesAndReencodesToTheSameBytes` |
