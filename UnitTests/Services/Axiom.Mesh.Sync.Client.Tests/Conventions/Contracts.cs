using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Sync.Client.Tests.Conventions;

/// <summary>The family's conventions, held by Axiom.Mesh.Sync.Client: one nested class per shared rule set, anchored on its own package.</summary>
public static class Contracts
{
    /// <summary>Axiom.Mesh.Sync.Client references only its layer, holds no mutable static state and exposes no foreign framework type.</summary>
    public sealed class PackageConventions : ConventionTests<Binding>
    {
        /// <inheritdoc />
        protected override bool UsesJson => false;
    }

    /// <summary>Axiom.Mesh.Sync.Client's public surface can grow without breaking a compiled consumer.</summary>
    public sealed class PublicSurface : PublicSurfaceTests<Binding>;

    /// <summary>A Client library is what apps reference: a client of the local protocol, never the engine or the daemon.</summary>
    public sealed class ClientOnly : ServiceOrAppConventionTests<Binding>;

    /// <summary>The suite of Axiom.Mesh.Sync.Client keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
