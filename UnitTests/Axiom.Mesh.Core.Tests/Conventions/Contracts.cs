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
        protected override IEnumerable<MeshException> Samples() =>
        [
            new MeshStoreException(
                "The node's database was not made by this version of Axiom.Mesh (schema version 1 expected, found 2).",
                "Run the version of Axiom.Mesh that made this database, or a later one that can migrate it.",
                "Or move the data directory aside and start anew."),
            new MeshNodeException(
                MeshFault.Environment,
                "The key store's device key (3f2a…) is not the device this database belongs to (9b10…).",
                "Start the node with the key store and the data directory that were made together."),
        ];
    }

    /// <summary>The suite of Axiom.Mesh.Core keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
