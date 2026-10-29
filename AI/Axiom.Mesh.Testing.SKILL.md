<identity>
    <package>Axiom.Mesh.Testing</package>
    <role>addon-skill</role>
    <requires>Axiom.Mesh.Host.SKILL.md</requires>
</identity>

<objective>
    Teach an agent to test Axiom.Mesh scenarios in memory: many nodes, one fake clock, no sockets.
</objective>

<package_scope>
    Namespace Axiom.Mesh.Testing. Today: TestKeys (empty). Planned (M6, M11): the mesh harness with its options, FakeDiscovery with Partition and Heal, in-memory dialling, ScriptedPeer, the CLI run in-process, waits backed by subscriptions, SettleAsync. Status: phase 1a skeleton; read the package README for what exists today. Every rule cites the section of SPEC.md or LOCAL-SPEC.md it implements, and its tests take expected values from that section.
</package_scope>

<architecture>
    Every node runs LWSE InMemory(), an in-memory LDB and in-memory object files under one FakeTimeProvider. Scenarios assert end states, never interleavings.
</architecture>

<best_practices>
    Wait on a subscription or SettleAsync, never on a sleep or a delay (a convention test refuses them). Seed ids through the options so outputs are reproducible.
</best_practices>

<pitfalls>
    A type named Mesh inside a namespace below Axiom collides with the namespace Axiom.Mesh; choose the harness's name with that in mind.
</pitfalls>
