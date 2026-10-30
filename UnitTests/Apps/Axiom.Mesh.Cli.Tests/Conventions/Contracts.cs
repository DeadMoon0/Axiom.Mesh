using System.Collections.Generic;
using Axiom.Mesh.Cli;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Cli.Tests.Conventions;

/// <summary>The family's conventions, held by Axiom.Mesh.Cli: one nested class per shared rule set, anchored on its own package.</summary>
public static class Contracts
{
    /// <summary>Axiom.Mesh.Cli references only its layer, holds no mutable static state and exposes no foreign framework type.</summary>
    public sealed class PackageConventions : ConventionTests<CliExitCode>
    {
        /// <inheritdoc />
        protected override bool UsesJson => true;
    }

    /// <summary>Axiom.Mesh.Cli's public surface can grow without breaking a compiled consumer.</summary>
    public sealed class PublicSurface : PublicSurfaceTests<CliExitCode>;

    /// <summary>Every exception Axiom.Mesh.Cli throws tells the reader who must act and what to do next.</summary>
    public sealed class ExceptionRecoveryContract : ExceptionRecoveryContractTests<CliExitCode>
    {
        /// <inheritdoc />
        protected override IEnumerable<MeshException> Samples() => [];
    }

    /// <summary>The command line is a client of the local protocol and reaches nothing of the engine or the daemon.</summary>
    public sealed class ServiceOrAppConventions : ServiceOrAppConventionTests<CliExitCode>;

    /// <summary>The suite of Axiom.Mesh.Cli keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
