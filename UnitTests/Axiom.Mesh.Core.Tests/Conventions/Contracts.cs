using System.Collections.Generic;
using Axiom.Mesh.Core;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Core.Tests.Conventions;

/// <summary>The family's conventions, held by Axiom.Mesh.Core: one nested class per shared rule set, anchored on its own package.</summary>
public static class Contracts
{
    /// <summary>Axiom.Mesh.Core references only its layer, holds no mutable static state and exposes no foreign framework type.</summary>
    public sealed class PackageConventions : ConventionTests<MeshNode>
    {
        /// <inheritdoc />
        protected override bool UsesJson => false;
    }

    /// <summary>Axiom.Mesh.Core's public surface can grow without breaking a compiled consumer.</summary>
    public sealed class PublicSurface : PublicSurfaceTests<MeshNode>;

    /// <summary>Every exception Axiom.Mesh.Core throws tells the reader who must act and what to do next.</summary>
    public sealed class ExceptionRecoveryContract : ExceptionRecoveryContractTests<MeshNode>
    {
        /// <inheritdoc />
        protected override IEnumerable<MeshException> Samples() => [];
    }

    /// <summary>The suite of Axiom.Mesh.Core keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
