<identity>
    <package>Axiom.Mesh.Model</package>
    <role>core-skill</role>
    <requires>none</requires>
</identity>

<objective>
    Teach an agent the honest core of Axiom.Mesh: ids, objects, Entries and signatures, order and validity rules, the exception family and the protocol limits.
</objective>

<package_scope>
    Namespace Axiom.Mesh. Today: MeshException (FriendlyMessage, RecoverySteps, AvailableOptions, Fault, IsTransient, [MESH ERROR] layout), MeshFault, MeshConfigurationException, Limits. Planned (M1): Hash32, ids, Signature64, PublicKey, ISigner, the object typestate HashedObject, ParsedObject, Verified*, StorableObject, payloads, drafts, Chunker, order and parent rules. Status: phase 1a skeleton; read the package README for what exists today. Every rule cites the section of SPEC.md or LOCAL-SPEC.md it implements, and its tests take expected values from that section.
</package_scope>

<architecture>
    Pure functions of bytes, keys and timestamps: no socket, disk, clock, randomness, LWSE or LDB. Depends on Axiom.ProtoStream (WireReader/WireWriter) only. A StorableObject is minted only inside Model, after verification; nothing else reaches the store.
</architecture>

<best_practices>
    Throw a MeshException only for caller, configuration or environment faults; a peer's mistake is a value (a violation or an ObjectFault). Use Try... methods for what might not be there. Every bound is a Limits constant with the spec's value.
</best_practices>

<pitfalls>
    Never re-encode before hashing or verifying: hash the bytes received (SPEC.md 2.2). BCL ECDSA is not deterministic, so signed vectors are verified, never byte-compared (STRUCTURAL-DEBT.md entry 1).
</pitfalls>
