using System.Collections.Generic;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Tests.Conventions;

/// <summary>The family's conventions, held by Axiom.Mesh.Model: one nested class per shared rule set, anchored on its own package.</summary>
public static class Contracts
{
    /// <summary>Axiom.Mesh.Model references only its layer, holds no mutable static state and exposes no foreign framework type.</summary>
    public sealed class PackageConventions : ConventionTests<MeshException>
    {
        /// <inheritdoc />
        protected override bool UsesJson => false;
    }

    /// <summary>Axiom.Mesh.Model's public surface can grow without breaking a compiled consumer.</summary>
    public sealed class PublicSurface : PublicSurfaceTests<MeshException>;

    /// <summary>Every exception Axiom.Mesh.Model throws tells the reader who must act and what to do next.</summary>
    public sealed class ExceptionRecoveryContract : ExceptionRecoveryContractTests<MeshException>
    {
        /// <inheritdoc />
        protected override IEnumerable<MeshException> Samples() => [new MeshConfigurationException("The configuration key mesh.prot is not known.", "Remove the key or correct its spelling.") { AvailableOptions = ["mesh.port"] }];
    }

    /// <summary>The suite of Axiom.Mesh.Model keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
