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
| §6 Every wait a deadline, every queue a bound | Not yet built (M6–M8). |
| §7 A failure says who must act and whether a retry can succeed | `Axiom.Mesh.Model/MeshException.cs` (`Fault`, `IsTransient`, `RecoverySteps` never empty, `AvailableOptions`, the `[MESH ERROR]` layout), `MeshFault.cs`, `MeshConfigurationException.cs` (one step required by its signature); `UnitTests/Shared/ExceptionRecoveryContractTests.cs` in every suite. The CLI's one-line explanations are not yet built (M10). |
| §8 Rules are pure functions tested from the spec; edges injected | `UnitTests/Shared/TestSuiteConventionTests.cs` (three parts per suite, no wall-clock waits, `// Damage:` lines, commented theory rows); `Axiom.Mesh.Host/HostProgram.cs` and `Apps/Axiom.Mesh.Cli/CliProgram.cs` (commands as functions of arguments, version and writers; `Program` is the shell); `Axiom.Mesh.Model/Rules/` (the deciding rules of SPEC.md §4.5, §8.6–§8.9 and §9 as pure functions of ids, clocks, objects and timestamps: `EntryAdmission`, `ParentRule`, `Lamport`, `HeadRule`, `CurrentObject`, `RosterRules`, `Visibility`, `Redactions`, `DuplicateSessions`, `ChunkListRule`, each tested in `Model.Tests/Rules` with expected values quoted from its section). |
| §9 A promise only with its test, a number only with its command | `Documentation/GUARANTEES.md` with `GuaranteeTests`; `Documentation/CONFORMANCE.md` with `ConformanceDocTests`; `Documentation/SECURITY.md`. Not yet built: `BENCHMARKS.md` (M12). |
| §10 The surface grows without breaking; libraries trimmed and AOT-safe | `Directory.Build.props` (`IsTrimmable`, `IsAotCompatible`, `EnablePackageValidation`, warnings as errors); `UnitTests/Shared/PublicSurfaceTests.cs` (no optional parameters, no open positional records; `Shared/ClosedShapeAttribute.cs`); `ConventionTests.PublicSignaturesUseNoForeignFrameworkTypes`, `HoldsNoMutableStaticState`, `EveryEnumHasAnExplicitZeroMember`. Not yet built: `aot.yml` (M12). |
| §11 Opt-outs are named options logged at start | Not yet built: the Host configuration (M8). |

Repository-wide conventions that serve several rules: `RepositoryConventionTests.EveryFileDeclaresExactlyOneTopLevelTypeNamedLikeTheFile`
and `ReadmeCompileTests` (in `UnitTests/Axiom.Mesh.ReadmeSamples.Tests`).

## Milestones

The milestone numbers above are those of the fundamentals plan: M1 Model, M2 Protocol and PAIR-SPEC, M3 Local,
M4 Discovery, M5 Core store and trust, M6 Core transport, M7 replication, M8 local port and Host, M9 requests, M10
pairing and CLI, M11 scenarios and integration, M12 benchmarks, security conformance and AOT, M13 docs and release.

## Axiom.Mesh.Protocol

The carriers of the protocol package (M2), by rule; under their own heading so that parallel milestones merge cleanly.

| Rule | Carried by |
|---|---|
| §1 Honest layers | `Axiom.Mesh.Protocol`: definitions, codecs and pure rules over Model's types; no socket, clock or randomness (`Rules/`, `Pairing/PairingMachine.cs` take timestamps). |
| §4 Nothing believed without a hash, a signature or a roster | `Messages/ObjectMessage.cs` (a received object's hash verified on the frame view before the message exists; `From(StorableObject)` the only way to send one), `Rules/HelloRule.cs` (SPEC.md §7.1 against the TLS key and the trust snapshot), `Pairing/PairingRules.cs` (a peer's Roster verified under its own user key and listing its TLS key's device; an enrolment signed, newer and listing the device), `Pairing/Sas.cs` (both users compare a string over every key). |
| §5 Every limit a named constant, enforced where the bytes enter | `AmpFrameCodec.cs` and `Messages/MessageFields.cs` (every list, string and blob of SPEC.md §7 checked against `Limits` and the bytes left before it is read or allocated); `Amp.cs` and `Pairing/PairProtocol.cs` (frame bound, buffer, timeouts from `Limits`); `Pairing/PairingCode.cs` (`MaxLength`, `MaxAddresses`). |
| §6 Every wait a deadline, every queue a bound | `Amp.Definition` (first `Hello` 10 s, idle 5 min, heartbeat 60 s), `PairProtocol.Definition` (10 s, 10 min), `Rules/ListingRule.cs` (`Deadline`), `Rules/WantBudget.cs` (64), `Rules/ObjectQueueRule.cs` (8 MiB), `Pairing/PairingMachine.cs` (`Expires`). Not carried: the 30-second frame completion of SPEC.md §5.1 (the session handler, M6/M7). |
| §8 Rules are pure functions tested from the spec | `Rules/` and `Pairing/`, each tested in `UnitTests/Axiom.Mesh.Protocol.Tests/Rules` with expected values quoted from SPEC.md, LOCAL-SPEC.md or PAIR-SPEC.md. |
| §9 A promise only with its test | `Documentation/PAIR-SPEC.md` §12 and the Protocol rows of CONFORMANCE.md and GUARANTEES.md. |
