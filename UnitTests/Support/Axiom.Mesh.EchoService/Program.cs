using Axiom.Mesh.Service;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.EchoService;

/// <summary>The process: one line over the service runtime, as every service's (LOCAL-SPEC.md §9.1).</summary>
internal static class Program
{
    private static int Main(string[] args) => ServiceProgram.Run(new TestEchoService(), args);
}
