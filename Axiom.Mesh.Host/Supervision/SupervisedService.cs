using System.Collections.Generic;

namespace Axiom.Mesh.Host;

/// <summary>What a <see cref="ServiceSupervisor"/> keeps running: the service's name, its resolved executable and arguments, and what its children are told.</summary>
/// <param name="Name">The service's name.</param>
/// <param name="Executable">The executable, absolute (<see cref="SpawnPathRule"/>).</param>
/// <param name="Arguments">The arguments after it.</param>
/// <param name="StateDirectory">The service's state directory (<see cref="HostPaths.ServiceState"/>), its children's working directory.</param>
/// <param name="Pipe">The pipe the daemon listens on, in full, for <c>AXIOM_MESH_PIPE</c>.</param>
/// <param name="Home">The daemon's home, for <c>AXIOM_MESH_HOME</c>.</param>
internal sealed record SupervisedService(string Name, string Executable, IReadOnlyList<string> Arguments, string StateDirectory, string Pipe, string Home);
