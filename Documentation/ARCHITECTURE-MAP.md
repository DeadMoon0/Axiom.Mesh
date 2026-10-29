# Architecture map

Names what carries each rule of [ARCHITECTURE.md](ARCHITECTURE.md) in the code today. Unlike the rules, this file
changes in the same commit as the code it describes. A rule with no carrier yet says **not yet built** and names the
milestone of the [fundamentals plan](#milestones) that builds it.

| Rule | Carried by |
|---|---|
| §1 Honest layers under one dishonest runtime | The layering itself: `Axiom.Mesh.Model`, `.Protocol`, `.Local` and `.Discovery` reference no runtime (`ConventionTests.ReferencesOnlyTheStatedFamilyPackages`). Not yet built: the Host as the one place time, sockets and disk enter (M8). |
| §2 Dependencies point downwards; apps and services reference nothing of the engine or the daemon | `UnitTests/Shared/ConventionTests.cs` (`AllowedReferences`, the table of plan §1, applied to every package's compiled references; guarded foreign assemblies: ProtectedData, the generic host and JSON configuration for the Host only); `UnitTests/Shared/ServiceOrAppConventionTests.cs` (`NoServiceOrAppReferencesCoreOrHost`, over the whole reference closure; derived by the CLI's suite); project references in each csproj. |
| §3 The Host alone holds keys, connections, store and database | Partly: the CLI references `Axiom.Mesh.Local` only (§2's tests). Not yet built: `NoKeyCrossesThePipeTests` (M9), the Host's key store (M8). |
| §4 Nothing believed without a hash, a signature or a roster | Not yet built: the object typestate of `Axiom.Mesh.Model` (M1), `MeshTls` and `HelloRule` (M2, M6), intake and the held buffer (M5). |
| §5 Every limit a named constant, enforced where the bytes enter | `Axiom.Mesh.Model/Limits.cs` (frame and object bounds so far); `Documentation/LIMITS.md`. Not yet built: the full set (M1), the protocols' definitions that enforce them (M2, M3), `EveryLimitOfTheSpecIsANamedConstantWithTheSpecsValue`. |
| §6 Every wait a deadline, every queue a bound | Not yet built (M6–M8). |
| §7 A failure says who must act and whether a retry can succeed | `Axiom.Mesh.Model/MeshException.cs` (`Fault`, `IsTransient`, `RecoverySteps` never empty, `AvailableOptions`, the `[MESH ERROR]` layout), `MeshFault.cs`, `MeshConfigurationException.cs` (one step required by its signature); `UnitTests/Shared/ExceptionRecoveryContractTests.cs` in every suite. The CLI's one-line explanations are not yet built (M10). |
| §8 Rules are pure functions tested from the spec; edges injected | `UnitTests/Shared/TestSuiteConventionTests.cs` (three parts per suite, no wall-clock waits, `// Damage:` lines, commented theory rows); `Axiom.Mesh.Host/HostProgram.cs` and `Apps/Axiom.Mesh.Cli/CliProgram.cs` (commands as functions of arguments, version and writers; `Program` is the shell). |
| §9 A promise only with its test, a number only with its command | `Documentation/GUARANTEES.md` with `GuaranteeTests`; `Documentation/CONFORMANCE.md` with `ConformanceDocTests`; `Documentation/SECURITY.md`. Not yet built: `BENCHMARKS.md` (M12). |
| §10 The surface grows without breaking; libraries trimmed and AOT-safe | `Directory.Build.props` (`IsTrimmable`, `IsAotCompatible`, `EnablePackageValidation`, warnings as errors); `UnitTests/Shared/PublicSurfaceTests.cs` (no optional parameters, no open positional records; `Shared/ClosedShapeAttribute.cs`); `ConventionTests.PublicSignaturesUseNoForeignFrameworkTypes`, `HoldsNoMutableStaticState`, `EveryEnumHasAnExplicitZeroMember`. Not yet built: `aot.yml` (M12). |
| §11 Opt-outs are named options logged at start | Not yet built: the Host configuration (M8). |

Repository-wide conventions that serve several rules: `RepositoryConventionTests.EveryFileDeclaresExactlyOneTopLevelTypeNamedLikeTheFile`
and `ReadmeCompileTests` (in `UnitTests/Axiom.Mesh.ReadmeSamples.Tests`).

## Milestones

The milestone numbers above are those of the fundamentals plan: M1 Model, M2 Protocol and PAIR-SPEC, M3 Local,
M4 Discovery, M5 Core store and trust, M6 Core transport, M7 replication, M8 local port and Host, M9 requests, M10
pairing and CLI, M11 scenarios and integration, M12 benchmarks, security conformance and AOT, M13 docs and release.
