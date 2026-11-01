using System.Collections.Generic;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Service.Tests.Conventions;

/// <summary>The family's conventions, held by Axiom.Mesh.Service: one nested class per shared rule set, anchored on its own package.</summary>
public static class Contracts
{
    /// <summary>Axiom.Mesh.Service references only its layer, holds no mutable static state and exposes no foreign framework type.</summary>
    public sealed class PackageConventions : ConventionTests<ServiceSession>
    {
        /// <inheritdoc />
        protected override bool UsesJson => true;
    }

    /// <summary>Axiom.Mesh.Service's public surface can grow without breaking a compiled consumer.</summary>
    public sealed class PublicSurface : PublicSurfaceTests<ServiceSession>;

    /// <summary>Every exception Axiom.Mesh.Service throws tells the reader who must act and what to do next.</summary>
    public sealed class ExceptionRecoveryContract : ExceptionRecoveryContractTests<ServiceSession>
    {
        /// <inheritdoc />
        protected override IEnumerable<MeshException> Samples() =>
        [
            new ServiceConfigurationException("Service chat finds no token.", "Set AXIOM_MESH_SERVICE_TOKEN, or list chat as external in config.json."),
        ];
    }

    /// <summary>The runtime of every service is a client of the local protocol: it reaches neither the engine nor the daemon.</summary>
    public sealed class ClientOnly : ServiceOrAppConventionTests<ServiceSession>;

    /// <summary>The suite of Axiom.Mesh.Service keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
