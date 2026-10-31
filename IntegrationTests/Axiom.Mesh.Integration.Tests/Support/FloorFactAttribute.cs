using System;

namespace Axiom.Mesh.Integration.Tests.Support;

/// <summary>
/// A release floor (fundamentals plan §5): a throughput bound measured on real sockets and disk, which runs only in a
/// Release build and only when the environment variable <c>AXIOM_MESH_FLOORS</c> is <c>1</c>, so the default run of the
/// suite stays inside the CI budget. Run them with
/// <c>AXIOM_MESH_FLOORS=1 dotnet test IntegrationTests/Axiom.Mesh.Integration.Tests -c Release --filter Category=Floor</c>.
/// </summary>
public sealed class FloorFactAttribute : FactAttribute
{
    /// <summary>The variable that switches the floors on.</summary>
    public const string Switch = "AXIOM_MESH_FLOORS";

    /// <summary>Skips the floor unless it is a Release build with the switch set.</summary>
    public FloorFactAttribute()
    {
#if DEBUG
        Skip = "A floor measures the shipped code: run it in a Release build.";
#else
        if (Environment.GetEnvironmentVariable(Switch) != "1")
            Skip = $"A floor runs only when {Switch}=1 (see FloorFactAttribute).";
#endif
    }
}
