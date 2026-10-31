using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Integration.Tests.Conventions;

/// <summary>The family's suite conventions, held by the integration suite as by every unit suite.</summary>
public static class Contracts
{
    /// <summary>The integration suite keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
