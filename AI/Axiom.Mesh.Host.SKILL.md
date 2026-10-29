<identity>
    <package>Axiom.Mesh.Host</package>
    <role>addon-skill</role>
    <requires>Axiom.Mesh.Core.SKILL.md</requires>
</identity>

<objective>
    Teach an agent the daemon axiom-meshd: its commands, configuration, keys and pipe.
</objective>

<package_scope>
    Namespace Axiom.Mesh.Host. Today: HostProgram.Run(args, output, error, version) with the command version, HostExitCode, and Program, the process shell. Planned (M8): run, config.json with unknown keys refused, FileKeyStore (DPAPI on Windows, 0600 on Linux), pipe listeners with process identity, logging. Status: phase 1a skeleton; read the package README for what exists today. Every rule cites the section of SPEC.md or LOCAL-SPEC.md it implements, and its tests take expected values from that section.
</package_scope>

<architecture>
    The only place time, sockets, disk and the pipe enter. Commands are functions of their inputs; Program reads what only a process has. The Host alone may reference ProtectedData, the generic host and JSON configuration.
</architecture>

<best_practices>
    Refuse a wrong configuration at start with recovery steps (MeshConfigurationException). Log the effective configuration once at start.
</best_practices>

<pitfalls>
    The assembly is Axiom.Mesh.Host; the published binary is to be named axiom-meshd (STRUCTURAL-DEBT.md entry 13).
</pitfalls>
