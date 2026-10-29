using System;

namespace Axiom.Mesh.Tests.Rules;

/// <summary>
/// A failure says who must act, whether a retry can succeed, and what to do next (ARCHITECTURE.md rule 7).
/// </summary>
public sealed class FailureGuidanceTests
{
    [Fact]
    public void AConfigurationFailureSaysTheConfigurationMustChange_AndThatRetryingUnchangedCannotSucceed()
    {
        var failure = new MeshConfigurationException("The port 70000 is not a TCP port.", "Set mesh.port to a value from 1 to 65535.");

        Assert.Equal(MeshFault.Configuration, failure.Fault);
        Assert.False(failure.IsTransient);
    }

    [Fact]
    public void AFailureCannotBeMadeWithoutAMessageOrWithABlankStep()
    {
        Assert.ThrowsAny<ArgumentException>(() => new MeshConfigurationException(" ", "Correct the key."));
        Assert.ThrowsAny<ArgumentException>(() => new MeshConfigurationException("The key is unknown.", " "));
        Assert.ThrowsAny<ArgumentException>(() => new MeshConfigurationException("The key is unknown.", "Correct the key.", ""));
    }

    [Fact]
    public void TheGuidanceKeepsEveryStepInOrder_AndListsWhatExistsWhenSomethingIsMissing()
    {
        var failure = new MeshConfigurationException("The configuration key mesh.prot is not known.", "Correct the spelling.", "Or remove the key.")
        {
            AvailableOptions = ["mesh.port", "mesh.mdns"],
        };

        Assert.Equal(["Correct the spelling.", "Or remove the key."], failure.RecoverySteps);
        Assert.Equal(["mesh.port", "mesh.mdns"], failure.AvailableOptions);
        Assert.StartsWith("The configuration key mesh.prot is not known.", failure.Message, StringComparison.Ordinal);
        Assert.Contains("mesh.mdns", failure.Message, StringComparison.Ordinal);
    }
}
