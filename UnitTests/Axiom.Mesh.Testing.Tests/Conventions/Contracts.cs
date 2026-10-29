using System.Collections.Generic;
using Axiom.Mesh.Testing;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Testing.Tests.Conventions;

/// <summary>The family's conventions, held by Axiom.Mesh.Testing: one nested class per shared rule set, anchored on its own package.</summary>
public static class Contracts
{
    /// <summary>Axiom.Mesh.Testing references only its layer, holds no mutable static state and exposes no foreign framework type.</summary>
    public sealed class PackageConventions : ConventionTests<TestMesh>
    {
        /// <inheritdoc />
        protected override bool UsesJson => false;
    }

    /// <summary>Axiom.Mesh.Testing's public surface can grow without breaking a compiled consumer.</summary>
    public sealed class PublicSurface : PublicSurfaceTests<TestMesh>;

    /// <summary>Every exception Axiom.Mesh.Testing throws tells the reader who must act and what to do next.</summary>
    public sealed class ExceptionRecoveryContract : ExceptionRecoveryContractTests<TestMesh>
    {
        /// <inheritdoc />
        protected override IEnumerable<MeshException> Samples() =>
        [
            new TestMeshException(
                "Waited 15 s for alice-laptop established with bob-desktop, in vain.",
                "Read the facts: a HandshakeFailed or RefusedBeforeHello names what was refused."),
        ];
    }

    /// <summary>The suite of Axiom.Mesh.Testing keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
