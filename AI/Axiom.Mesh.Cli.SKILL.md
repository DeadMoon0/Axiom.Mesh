<identity>
    <package>Axiom.Mesh.Cli</package>
    <role>addon-skill</role>
    <requires>Axiom.Mesh.Local.SKILL.md</requires>
</identity>

<objective>
    Teach an agent the command line axiom-mesh and how it explains failures.
</objective>

<package_scope>
    Namespace Axiom.Mesh.Cli. Today: CliProgram.Run(args, output, error, version) with --version, CliExitCode. Planned (M10): a pure CommandLine.Parse, the commands of plan 2.7, exit codes 0 ok, 1 usage, 2 Host not reachable, 3 refused, 4 timeout, --json on every list. Status: phase 1a skeleton; read the package README for what exists today. Every rule cites the section of SPEC.md or LOCAL-SPEC.md it implements, and its tests take expected values from that section.
</package_scope>

<architecture>
    A user client of Axiom.Mesh.Local only (no System.CommandLine, plan decision D9); writes Entries through the external cli service connection with the token from the Host's configuration. Packed as the .NET tool axiom-mesh.
</architecture>

<best_practices>
    Every known failure is one line naming what to do; ids accept full hex or an unambiguous prefix.
</best_practices>

<pitfalls>
    Never reference Core or Host (ServiceOrAppConventionTests); never print a stack trace for a known fault.
</pitfalls>
