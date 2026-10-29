using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.ReadmeSamples.Tests.Conventions;

/// <summary>The family's conventions this suite holds: it has no package of its own, only its test sources.</summary>
public static class Contracts
{
    /// <summary>This suite keeps the three parts apart and never waits by the wall clock.</summary>
    public sealed class TestSuiteConventions : TestSuiteConventionTests;
}
