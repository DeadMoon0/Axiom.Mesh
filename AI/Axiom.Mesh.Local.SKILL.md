<identity>
    <package>Axiom.Mesh.Local</package>
    <role>addon-skill</role>
    <requires>Axiom.Mesh.Model.SKILL.md</requires>
</identity>

<objective>
    Teach an agent axiom-mesh-local/1 and its client library: how an app, a script or a service talks to the Host over the pipe.
</objective>

<package_scope>
    Namespace Axiom.Mesh.Local. Today: LocalProtocol.Name. Planned (M3): message classes by id range, one sealed class per request with IRequest<TSelf,TOk>, the records of LOCAL-SPEC.md 5, LocalProtocol.HostSide and ClientSide, LocalClient (SendAsync to LocalResult, SubscribeAsync to IAsyncEnumerable), Reconnect.Delay, PipeLocation.Resolve, LocalPipe. Status: phase 1a skeleton; read the package README for what exists today. Every rule cites the section of SPEC.md or LOCAL-SPEC.md it implements, and its tests take expected values from that section.
</package_scope>

<architecture>
    Honest except LocalPipe, the one socket/pipe shell. Depends on Model and ProtoStream; never on Protocol, Core, Host, LWSE or LDB. Statuses and errors are values; enumerated fields keep unknown values (LOCAL-SPEC.md 1.5).
</architecture>

<best_practices>
    Reference this package, and nothing of Core or Host, from any client or service. Test clients over an in-memory stream pair.
</best_practices>

<pitfalls>
    A service presents its token in Hello.token: a spawned service gets it from AXIOM_MESH_SERVICE_TOKEN, an external one (the CLI) from the Host's configuration (LOCAL-SPEC.md 4.2). No record may carry a private key.
</pitfalls>
