using System;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Chat.Tests.Conventions;

/// <summary>The family's conventions, held by the chat service: one nested class per shared rule set, anchored on its own executable.</summary>
public static class Contracts
{
    /// <summary>Axiom.Mesh.Chat references only its layer, holds no mutable static state and exposes no foreign framework type.</summary>
    public sealed class PackageConventions : ConventionTests<ChatService>
    {
        /// <inheritdoc />
        protected override bool UsesJson => false;
    }

    /// <summary>Axiom.Mesh.Chat's public surface can grow without breaking a compiled consumer.</summary>
    public sealed class PublicSurface : PublicSurfaceTests<ChatService>;

    /// <summary>The chat service is a client of the local protocol: it reaches neither the engine nor the daemon.</summary>
    public sealed class ClientOnly : ServiceOrAppConventionTests<ChatService>;

    /// <summary>A service owns no user interface (S-D13): its namespace, typed in Axiom.Mesh.Chat.Client, is its only surface.</summary>
    public sealed class ServiceConventions : ServiceConventionTests<ChatService>
    {
        /// <inheritdoc />
        protected override Type ClientAnchor => typeof(ChatCall);

        /// <inheritdoc />
        protected override Type Methods => typeof(ChatMethods);

        /// <inheritdoc />
        protected override Type Events => typeof(ChatEvents);
    }

    /// <summary>The suite of the chat service keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
