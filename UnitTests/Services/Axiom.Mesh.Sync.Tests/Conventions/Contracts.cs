using System;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Sync.Tests.Conventions;

/// <summary>The family's conventions, held by the sync service: one nested class per shared rule set, anchored on its own executable.</summary>
public static class Contracts
{
    /// <summary>Axiom.Mesh.Sync references only its layer, holds no mutable static state and exposes no foreign framework type.</summary>
    public sealed class PackageConventions : ConventionTests<SyncService>
    {
        /// <inheritdoc />
        protected override bool UsesJson => false;
    }

    /// <summary>Axiom.Mesh.Sync's public surface can grow without breaking a compiled consumer.</summary>
    public sealed class PublicSurface : PublicSurfaceTests<SyncService>;

    /// <summary>The sync service is a client of the local protocol: it reaches neither the engine nor the daemon.</summary>
    public sealed class ClientOnly : ServiceOrAppConventionTests<SyncService>;

    /// <summary>A service owns no user interface (S-D13): its namespace, typed in Axiom.Mesh.Sync.Client, is its only surface.</summary>
    public sealed class ServiceConventions : ServiceConventionTests<SyncService>
    {
        /// <inheritdoc />
        protected override Type ClientAnchor => typeof(SyncCall);

        /// <inheritdoc />
        protected override Type Methods => typeof(SyncMethods);

        /// <inheritdoc />
        protected override Type Events => typeof(SyncEvents);
    }

    /// <summary>The suite of the sync service keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
