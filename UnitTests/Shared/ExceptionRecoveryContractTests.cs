using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Tests.Shared;

/// <summary>
/// Sweeps every exception type a package ships: each carries a message and recovery steps, states who must act and
/// whether a retry can succeed, and formats into readable sections. A per-type test passes while a new exception type
/// quietly ships without guidance; the sweep is what notices, because it also fails when a type has no sample here.
/// </summary>
/// <typeparam name="TAnchor">Any public type of the package under test.</typeparam>
public abstract class ExceptionRecoveryContractTests<TAnchor>
{
    /// <summary>One instance of every concrete <see cref="MeshException"/> the package declares.</summary>
    /// <returns>The samples; empty for a package that declares none.</returns>
    protected abstract IEnumerable<MeshException> Samples();

    [Fact]
    public void EveryExceptionTypeOfThePackageHasASample()
    {
        var declared = typeof(TAnchor).Assembly.GetTypes()
            .Where(t => typeof(MeshException).IsAssignableFrom(t) && !t.IsAbstract)
            .ToHashSet();
        var sampled = Samples().Select(e => e.GetType()).ToHashSet();

        var missing = declared.Except(sampled).Select(t => t.FullName).ToList();
        Assert.True(missing.Count == 0, "Exception types without a sample in the recovery sweep: " + string.Join(", ", missing));
    }

    // Every public exception of the package is in the family, so a caller can rely on the recovery contract.
    [Fact]
    public void EveryPublicExceptionIsInTheFamily()
    {
        var outside = typeof(TAnchor).Assembly.GetExportedTypes()
            .Where(t => typeof(Exception).IsAssignableFrom(t) && !typeof(MeshException).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();
        Assert.True(outside.Count == 0, "Public exceptions outside MeshException: " + string.Join(", ", outside));
    }

    [Fact]
    public void EveryExceptionCarriesTheFullRecoveryContract()
    {
        foreach (MeshException exception in Samples())
        {
            string name = exception.GetType().Name;
            Assert.False(string.IsNullOrWhiteSpace(exception.FriendlyMessage), $"{name} has no message.");
            Assert.NotEmpty(exception.RecoverySteps);
            Assert.True(Enum.IsDefined(exception.Fault), $"{name} states an undefined fault.");

            string formatted = exception.ToString();
            Assert.Contains("[MESH ERROR] " + name, formatted, StringComparison.Ordinal);
            Assert.Contains("Recovery:", formatted, StringComparison.Ordinal);
            Assert.Contains("  -> ", formatted, StringComparison.Ordinal);
            Assert.Contains("Fault: " + exception.Fault, formatted, StringComparison.Ordinal);
            Assert.True(formatted.Split(Environment.NewLine).Length > 3, $"{name} formatted to fewer than four lines.");
        }
    }
}
