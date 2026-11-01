using Axiom.Mesh.Service;

namespace Axiom.Mesh.Sync;

/// <summary>The process: one line over the service runtime (LOCAL-SPEC.md §9.1).</summary>
internal static class Program
{
    private static int Main(string[] args) => ServiceProgram.Run(new SyncService(), args);
}
