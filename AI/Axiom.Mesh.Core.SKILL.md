<identity>
    <package>Axiom.Mesh.Core</package>
    <role>core-skill</role>
    <requires>Axiom.Mesh.Model.SKILL.md, Axiom.Mesh.Protocol.SKILL.md, Axiom.Mesh.Local.SKILL.md</requires>
</identity>

<objective>
    Teach an agent the engine of an Axiom.Mesh node and its thread-safety model.
</objective>

<package_scope>
    Namespace Axiom.Mesh.Core. Today: MeshNode (not startable). Planned (M5 to M9): MeshNode.StartAsync with MeshNodeEnvironment (every dishonest dependency injected), the LDB entities, ObjectStore, StoreWriter, TrustSnapshot, Intake and HeldBuffer, MeshTls, PeerTable, replication, LocalPort and the request dispatcher. Status: phase 1a skeleton; read the package README for what exists today. Every rule cites the section of SPEC.md or LOCAL-SPEC.md it implements, and its tests take expected values from that section.
</package_scope>

<architecture>
    One writer, immutable snapshots, per-session actors (plan decision D8): StoreWriter alone writes; TrustSnapshot is swapped whole and read lock-free. One LWSE server with three ports (mesh, outbound adopt-only, local adopt-only). Depends on Model, Protocol, Local, Discovery, ProtoStream, LDB and, once published, LWSE 0.3.0.
</architecture>

<best_practices>
    Every wait has a deadline, every queue a bound; the Host never blocks on a peer or a client. Requests outside the phase-1 set answer UnknownRequest.
</best_practices>

<pitfalls>
    Do not reference Axiom.LWSE.Core before 0.3.0 is on nuget.org. Nothing unverified is stored, held past its bound, served or forwarded.
</pitfalls>
