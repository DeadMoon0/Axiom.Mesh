using System.Collections.Generic;
using Axiom.Mesh.Local;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Local.Tests.Conventions;

/// <summary>The family's conventions, held by Axiom.Mesh.Local: one nested class per shared rule set, anchored on its own package.</summary>
public static class Contracts
{
    /// <summary>Axiom.Mesh.Local references only its layer, holds no mutable static state and exposes no foreign framework type.</summary>
    public sealed class PackageConventions : ConventionTests<LocalProtocol>
    {
        /// <inheritdoc />
        protected override bool UsesJson => false;
    }

    /// <summary>Axiom.Mesh.Local's public surface can grow without breaking a compiled consumer.</summary>
    public sealed class PublicSurface : PublicSurfaceTests<LocalProtocol>;

    /// <summary>Every exception Axiom.Mesh.Local throws tells the reader who must act and what to do next.</summary>
    public sealed class ExceptionRecoveryContract : ExceptionRecoveryContractTests<LocalProtocol>
    {
        /// <inheritdoc />
        protected override IEnumerable<MeshException> Samples() => [];
    }

    /// <summary>The suite of Axiom.Mesh.Local keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
