# Architecture map

Names what carries each rule of [ARCHITECTURE.md](ARCHITECTURE.md) in the code today. Unlike the rules, this file
changes in the same commit as the code it describes. A rule with no carrier yet says **not yet built** and names the
milestone of the [fundamentals plan](#milestones) that builds it.

| Rule | Carried by |
|---|---|
| §1 Honest layers under one dishonest runtime | The layering itself: `Axiom.Mesh.Model`, `.Protocol`, `.Local` and `.Discovery` reference no runtime (`ConventionTests.ReferencesOnlyTheStatedFamilyPackages`). Not yet built: the Host as the one place time, sockets and disk enter (M8). |
| §2 Dependencies point downwards; apps and services reference nothing of the engine or the daemon | `UnitTests/Shared/ConventionTests.cs` (`AllowedReferences`, the table of plan §1, applied to every package's compiled references; guarded foreign assemblies: ProtectedData, the generic host and JSON configuration for the Host only); `UnitTests/Shared/ServiceOrAppConventionTests.cs` (`NoServiceOrAppReferencesCoreOrHost`, over the whole reference closure; derived by the CLI's suite); project references in each csproj. |
| §3 The Host alone holds keys, connections, store and database | Partly: the CLI references `Axiom.Mesh.Local` only (§2's tests). Not yet built: `NoKeyCrossesThePipeTests` (M9), the Host's key store (M8). |
| §4 Nothing believed without a hash, a signature or a roster | The object typestate of `Axiom.Mesh.Model`: `Objects/HashedObject.cs` (bytes with their proven hash, the only way into `Objects.TryParse`), `Objects/ParsedObject.cs` and `Objects/StorableObject.cs` (private-protected constructors; every subclass sealed with an internal constructor), `Objects/Verified{Manifest,Roster,Grant,Entry}.cs` (`TryVerify` against the key SPEC.md §8 names, over the bytes received), `Rules/EntryAdmission.cs` (the only way to an `AdmittedEntry`), `Primitives/Signature64.cs` and `Primitives/PublicKey.cs` (low-s and on-curve by construction); proven by `StorableObjectSurfaceTests.NoStorableObjectCanBeMadeOutsideModelWithoutVerification`. Not yet built: `MeshTls` and `HelloRule` (M2, M6), intake and the held buffer (M5). |
| §5 Every limit a named constant, enforced where the bytes enter | `Axiom.Mesh.Model/Limits.cs` (every bound of SPEC.md and LOCAL-SPEC.md §12), `Documentation/LIMITS.md` (its Constant column), `Axiom.Mesh.Model/Wire.cs` (every string, blob and list reader takes its maximum and checks the length before the content); `LimitsDocumentTests.EveryLimitOfTheSpecIsANamedConstantWithTheSpecsValue` and `LimitsTests.EveryLimitHasTheValueItsSectionStates` in `Model.Tests`. Not yet built: the protocols' definitions that enforce the message bounds (M2, M3). |
| §6 Every wait a deadline, every queue a bound | Partly: the store (M5), the transport (M6) and replication (M7), see the Core section; the local port follows (M8). |
| §7 A failure says who must act and whether a retry can succeed | `Axiom.Mesh.Model/MeshException.cs` (`Fault`, `IsTransient`, `RecoverySteps` never empty, `AvailableOptions`, the `[MESH ERROR]` layout), `MeshFault.cs`, `MeshConfigurationException.cs` (one step required by its signature); `UnitTests/Shared/ExceptionRecoveryContractTests.cs` in every suite. The CLI's one-line explanations are not yet built (M10). |
| §8 Rules are pure functions tested from the spec; edges injected | `UnitTests/Shared/TestSuiteConventionTests.cs` (three parts per suite, no wall-clock waits, `// Damage:` lines, commented theory rows); `Axiom.Mesh.Host/HostProgram.cs` and `Apps/Axiom.Mesh.Cli/CliProgram.cs` (commands as functions of arguments, version and writers; `Program` is the shell); `Axiom.Mesh.Model/Rules/` (the deciding rules of SPEC.md §4.5, §8.6–§8.9 and §9 as pure functions of ids, clocks, objects and timestamps: `EntryAdmission`, `ParentRule`, `Lamport`, `HeadRule`, `CurrentObject`, `RosterRules`, `Visibility`, `Redactions`, `DuplicateSessions`, `ChunkListRule`, each tested in `Model.Tests/Rules` with expected values quoted from its section). |
| §9 A promise only with its test, a number only with its command | `Documentation/GUARANTEES.md` with `GuaranteeTests`; `Documentation/CONFORMANCE.md` with `ConformanceDocTests`; `Documentation/SECURITY.md`. Not yet built: `BENCHMARKS.md` (M12). |
| §10 The surface grows without breaking; libraries trimmed and AOT-safe | `Directory.Build.props` (`IsTrimmable`, `IsAotCompatible`, `EnablePackageValidation`, warnings as errors); `UnitTests/Shared/PublicSurfaceTests.cs` (no optional parameters, no open positional records; `Shared/ClosedShapeAttribute.cs`); `ConventionTests.PublicSignaturesUseNoForeignFrameworkTypes`, `HoldsNoMutableStaticState`, `EveryEnumHasAnExplicitZeroMember`. Not yet built: `aot.yml` (M12). |
| §11 Opt-outs are named options logged at start | Not yet built: the Host configuration (M8). |

## Axiom.Mesh.Local

| Rule | Carried by |
|---|---|
| §1 Honest layers | Everything of `Axiom.Mesh.Local` is a function of its inputs except `Pipe/LocalPipe.cs`, the one place it touches the OS (finding and connecting to the pipe); `PipeLocation.Resolve` and `Reconnect.Delay` are pure. |
| §2 Apps and services reach nothing of the engine | `Conventions/SurfaceTests.NoCoreTypeIsReachableFromLocalsPublicSurface` in `Local.Tests`; the package references Model and ProtoStream only. |
| §3 No key crosses the pipe | `Conventions/NoKeyCrossesThePipeTests` in `Local.Tests`; `Messages/ServiceToken.cs` (a token opens one service name, compares in fixed time, never prints). |
| §4 Nothing believed without proof | Not applicable: a client verifies nothing (LOCAL-SPEC.md §1.4); the Host derives every record from verified objects. |
| §5 Every limit a named constant, enforced where the bytes enter | `Codec/LocalFrameCodecOfTInTOut.cs` (classifies by id range before the body, reads every body exactly to its end, keeps the correlation rules on the Host side), `Wire/FieldBounds.cs` (every field bound of the layouts, the limits of `Limits` where they coincide), `Wire/LocalWire.cs` (counts and lengths checked before content), `LocalProtocol.cs` (one framer of `Limits.MaxFrameLength`); `Rules/LimitTests`, `Rules/BoundsTests`. |
| §6 Every wait a deadline, every queue a bound | `LocalProtocol.cs` (first message 10 s, idle backstop 120 s), `Client/LocalClient.cs` (at most 64 outstanding requests), `Client/SubscriptionOfTEvent.cs` (256 queued events, then the subscription lapses alone), `Pipe/LocalPipe.cs` (5 s connect). |
| §7 A failure says who must act | `Client/LocalConnectionException.cs` (refused, closed by the Host, lost; fault and transience per cause), `Client/RequestLimitException.cs`; refusals are `LocalResult<TOk>` values. |
| §8 Rules are pure and tested from the spec | `Local.Tests/Rules/` (class rules, correlations, bounds, tolerance, Hello layout, epoch, reconnect, pipe location, limits). |
| §10 The surface grows without breaking | Requests, events and records are sealed records with `init` properties; the frame and body classes are closed (private-protected constructors). |

Repository-wide conventions that serve several rules: `RepositoryConventionTests.EveryFileDeclaresExactlyOneTopLevelTypeNamedLikeTheFile`
and `ReadmeCompileTests` (in `UnitTests/Axiom.Mesh.ReadmeSamples.Tests`).

## Milestones

The milestone numbers above are those of the fundamentals plan: M1 Model, M2 Protocol and PAIR-SPEC, M3 Local,
M4 Discovery, M5 Core store and trust, M6 Core transport, M7 replication, M8 local port and Host, M9 requests, M10
pairing and CLI, M11 scenarios and integration, M12 benchmarks, security conformance and AOT, M13 docs and release.

## Axiom.Mesh.Discovery

What carries the rules in `Axiom.Mesh.Discovery` (M4).

| Rule | Carried by |
|---|---|
| §1 Honest layers under one dishonest runtime | `Codecs/` (`DnsSdCodec`, `BroadcastCodec`) and `Rules/` (`AnnounceGate`, `AnnouncementCache`) are pure: bytes, ids and timestamps in, values out. `Shells/` (`MdnsDiscovery`, `BroadcastDiscovery`, `StaticPeers`, `CompositeDiscovery`) are the only code that touches sockets, interfaces, DNS or a `TimeProvider`, and they only bind, receive, send and record. |
| §2 Dependencies point downwards | References `Axiom.Mesh.Model` only, for `DeviceId`, `UserId` and `Hash32` (plan decision D2); BCL sockets, no library. |
| §4 Nothing believed without a hash, a signature or a roster | An `Announcement` is a claim, documented as a hint; the codecs refuse an all-zero id so nothing from the network can pose as a `Static` peer; the dialler (M6) decides by roster before any TCP connection. |
| §5 Every limit a named constant, enforced where the bytes enter | `BroadcastCodec.Length` (71), `DnsSdCodec.MaxPacketLength` (9000), `MaxRecords`, `MaxInstances`, `MaxAddresses`, name length 255 and 16 pointer jumps inside the reader, `AnnounceGate.Interval` (1 s, SPEC.md §3.2); the shells' receive buffers are one byte over the bound so a longer datagram is seen and refused. |
| §6 Every wait a deadline, every queue a bound | Each observer reads from its own bounded queue (256, oldest dropped); `DiscoveryFacts` is bounded; `AnnouncementCache` is bounded; nothing a shell does blocks on an observer. |
| §7 A failure says who must act | A socket, interface or name that fails is a `DiscoveryFact` (kind, subject, the platform's reason) for the engine to log; nothing on the network throws. |
| §8 Rules are pure functions tested from the spec | `UnitTests/Axiom.Mesh.Discovery.Tests/Rules` (SPEC.md §3.1–§3.3, DESIGN.md §6.1/§14 quoted); `Regression/` pins the wire bytes against vectors from an independent script, fuzzes both parsers (seed 42 × 20 000) and runs one loopback UDP smoke test. mDNS on a real LAN is checked by hand (STRUCTURAL-DEBT.md entry 2). |

## Axiom.Mesh.Protocol

The carriers of the protocol package (M2), by rule; under their own heading so that parallel milestones merge cleanly.

| Rule | Carried by |
|---|---|
| §1 Honest layers | `Axiom.Mesh.Protocol`: definitions, codecs and pure rules over Model's types; no socket, clock or randomness (`Rules/`, `Pairing/PairingMachine.cs` take timestamps). |
| §4 Nothing believed without a hash, a signature or a roster | `Messages/ObjectMessage.cs` (a received object's hash verified on the frame view before the message exists; `From(StorableObject)` the only way to send one), `Rules/HelloRule.cs` (SPEC.md §7.1 against the TLS key and the trust snapshot), `Pairing/PairingRules.cs` (a peer's Roster verified under its own user key and listing its TLS key's device; an enrolment signed, newer and listing the device), `Pairing/Sas.cs` (both users compare a string over every key). |
| §5 Every limit a named constant, enforced where the bytes enter | `AmpFrameCodec.cs` and `Messages/MessageFields.cs` (every list, string and blob of SPEC.md §7 checked against `Limits` and the bytes left before it is read or allocated); `Amp.cs` and `Pairing/PairProtocol.cs` (frame bound, buffer, timeouts from `Limits`); `Pairing/PairingCode.cs` (`MaxLength`, `MaxAddresses`). |
| §6 Every wait a deadline, every queue a bound | `Amp.Definition` (first `Hello` 10 s, idle 5 min, heartbeat 60 s), `PairProtocol.Definition` (10 s, 10 min), `Rules/ListingRule.cs` (`Deadline`), `Rules/WantBudget.cs` (64), `Rules/ObjectQueueRule.cs` (8 MiB), `Pairing/PairingMachine.cs` (`Expires`). The 30-second frame completion of SPEC.md §5.1 is Core's `Transport/LinkActivity.cs` (M7). |
| §8 Rules are pure functions tested from the spec | `Rules/` and `Pairing/`, each tested in `UnitTests/Axiom.Mesh.Protocol.Tests/Rules` with expected values quoted from SPEC.md, LOCAL-SPEC.md or PAIR-SPEC.md. |
| §9 A promise only with its test | `Documentation/PAIR-SPEC.md` §12 and the Protocol rows of CONFORMANCE.md and GUARANTEES.md. |

## Axiom.Mesh.Core

What the store, the index, trust and intake (M5), the transport (M6) and replication (M7) carry, by rule. The local
port joins in M8.

| Rule | Carried by |
|---|---|
| §1 Honest layers under one dishonest runtime | `Axiom.Mesh.Core/Seams/` (`IEntropy`, `SystemEntropy`, `DeterministicEntropy`) and `Storage/IObjectFiles.cs` (`DirectoryObjectFiles`, `InMemoryObjectFiles`): randomness and disk enter by these seams; the clock is a `TimeProvider` handed to the writer and to LDB; LDB is handed in as a `Database`. |
| §4 Nothing believed without a hash, a signature or a roster | `Intake/Intake.cs` (copy, `HashedObject.TryVerify`, `Objects.TryParse`, then `IntakeRules.Decide`), `Intake/IntakeRules.cs` (SPEC.md §10.6 by tag over Model's `Verified*.TryVerify` and `EntryAdmission`), `Store/Commands/AdmitCommand.cs` (the writer's only way in takes a `StorableObject`); `Trust/TrustSnapshot.cs` (`TryResolveDevice` pins a key by `H(point)` to a not-removed device of a known user). Proven by `IntakeTests` and `TrustSnapshotTests`. |
| §5 Every limit a named constant, enforced where the bytes enter | `Intake/HeldBuffer.cs` (`Limits.MaxHeldBytesPerSession`, `MaxHeldBytes`, `HeldAfterDisconnectSeconds`), `Store/StoreWriter.cs` (queue 4 096, batches of 256 commands or 8 MiB), `Store/ChunkStreamer.cs` (`Limits.ChunkLength`, `MaxChunkListChunks`). |
| §6 Every wait a deadline, every queue a bound | `Store/StoreWriter.cs` (one bounded queue; a full one makes senders wait), `Changes/ChangeBus.cs` and `ChangeSubscription.cs` (a bounded queue per subscriber that lapses alone, never blocking the writer), `Intake/HeldBuffer.cs`. |
| §7 A failure says who must act | `MeshStoreException.cs` (schema mismatch, a damaged current object, an unwritable store; Fault Environment, never transient). |
| §8 Rules are pure functions tested from the spec | `Intake/IntakeRules.cs`, `Store/RetentionRule.cs`, `Store/EntryIndex.cs` (keyset order), Model's `CurrentObject`, `RosterRules`, `HeadRule`, `Redactions` applied by `Store/StoreBatch.cs` and `Trust/TrustBuilder.cs`; the shells (`StoreWriter`, `Intake`, `ObjectStore`, `Evictor`) only sequence them. |
| D8 One writer, immutable snapshots | `Store/StoreWriter.cs` (the only writer of the index and the object files; one LDB transaction per batch, completions after the commit), `Trust/TrustSnapshot.cs` (frozen, swapped whole, read lock-free), `Store/WriterState.cs` (reloaded from the database after a failed commit). |
| §9 A number only with the command that measured it | `Regression/EntryIndexProbeTests.cs` prints the LDB probe (100 000 Entries) with `dotnet test --filter EntryIndexProbeTests --logger "console;verbosity=detailed" -c Release`. |
| §1 (transport) Sockets, keys and the clock are injected | `Seams/IKeyStore.cs`, `Seams/IDialer.cs` (`TcpDialer`), `Node/MeshNodeEnvironment.cs` (every dishonest part of a node, in-memory or bound); `Axiom.Mesh.Testing` fills it with in-memory parts on one fake clock. |
| §4 (transport) Nothing believed without a roster | `Transport/PeerCheck.cs` (the certificate callback resolves the P-256 point through `TrustSnapshot.TryResolveDevice`; after the handshake `Decide` admits a roster key under `axiom-mesh/1` and a pairing key under the pairing ALPN only), `Transport/MeshTlsLayer.cs` (TLS 1.3, client certificate required, no resumption, `PeerIdentity` set only on success), `Node/PeerSessionHandler.cs` (`HelloRule.Check` before anything but `Hello` is read). |
| §6 (transport) Every wait a deadline | `Transport/MeshTlsLayer.cs` (10-second handshake deadline on the node's clock), `Amp.Definition` (first `Hello` 10 s, idle 5 min, heartbeat 60 s), `Peers/PeerDialler.cs` (admission reserved before the dial, held 10 s), `Seams/TcpDialer.cs` (10-second connect), `Node/PeerSessionHandler.cs` (a `Bye` is written within 5 s). |
| §8 (transport) Rules are pure functions | `Peers/DialPolicy.cs`, `Peers/Backoff.cs`, `Peers/ViolationWindow.cs`, Model's `DuplicateSessions` and Protocol's `HelloRule`, applied by `Peers/PeerTable.cs` (one lock, no I/O inside, an immutable view swapped after each change, facts recorded after the lock). |
| §9 (transport) A promise only with its test | `Node/NodeFacts.cs`: every decision about a connection or a peer is a fact, so tests wait on what happened instead of on time. |
| §4 (replication) Nothing unverified stored, served or forwarded | `Replication/ReplicationSession.cs` (every received `Object` goes through `Intake`; a `Want` is answered only after `Replication/ServedObjects.cs` found the object stored and visible to the peer's user, SPEC.md §9.7–§9.8; `RosterQuery` through `TrustSnapshot.MayServeRoster`), `Replication/Listings.cs` (exactly the Packages `TrustSnapshot.MayList` allows, Grants per §9.7), `Store/StoreBatch.cs` (`HasParentFaultAsync`: an Entry whose held parents fail §8.6 is not stored), Protocol's `ObjectMessage.FromStored` called from the store's send path only (`Conventions/SendPathTests`). |
| §6 (replication) Every wait a deadline, every queue a bound | `Transport/LinkActivity.cs` (30-second frame completion, one timer per connection), `Replication/ReplicationSession.cs` (the `ListingRule` deadline as a timer; one change subscription of 1 024 sets that lapses into a full listing), `Replication/SessionOutbox.cs` (control lane; object lane of 8 MiB with deferral; `Have` dropped past 65 536 waiting hashes; the peer's 64 outstanding wants), `Replication/WantScheduler.cs` (64 outstanding per peer, a re-ask after 30 s on the node's clock), `Replication/SourceBook.cs` (65 536 claims per peer), `Replication/Replicator.cs` (held objects dropped 10 minutes after their sender left). |
| §8 (replication) Rules are pure functions | `Replication/ReplicationPlanner.cs` (`Plan`: SPEC.md §10.2 steps 1–3; `Walk`: §10.3 under `RetentionRule`), `Replication/Listings.cs`, Protocol's `ListingRule`, `WantBudget` and `ObjectQueueRule`, Model's `Lamport` and `Visibility`; the session, the scheduler and the replicator only sequence them. |
| D8 (replication) Per-session actors | `Replication/ReplicationSession.cs`: one read loop, one write loop, one push loop per session over immutable snapshots (`TrustSnapshot`, `Store/HeadsView.cs`); nothing a session does waits on another session. |
