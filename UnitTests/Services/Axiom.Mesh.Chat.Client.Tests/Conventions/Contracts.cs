using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Chat.Client.Tests.Conventions;

/// <summary>The family's conventions, held by Axiom.Mesh.Chat.Client: one nested class per shared rule set, anchored on its own package.</summary>
public static class Contracts
{
    /// <summary>Axiom.Mesh.Chat.Client references only its layer, holds no mutable static state and exposes no foreign framework type.</summary>
    public sealed class PackageConventions : ConventionTests<MessageView>
    {
        /// <inheritdoc />
        protected override bool UsesJson => false;
    }

    /// <summary>Axiom.Mesh.Chat.Client's public surface can grow without breaking a compiled consumer.</summary>
    public sealed class PublicSurface : PublicSurfaceTests<MessageView>;

    /// <summary>A Client library is what apps reference: a client of the local protocol, never the engine or the daemon.</summary>
    public sealed class ClientOnly : ServiceOrAppConventionTests<MessageView>;

    /// <summary>The suite of Axiom.Mesh.Chat.Client keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
