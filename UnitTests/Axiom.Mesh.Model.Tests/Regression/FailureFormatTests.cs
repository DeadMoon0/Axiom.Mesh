using System;

namespace Axiom.Mesh.Tests.Regression;

/// <summary>The layout a failure prints in, which logs, the CLI and the family's other packages share.</summary>
public sealed class FailureFormatTests
{
    // Damage: a reordered or renamed section breaks every reader who scans logs for "[MESH ERROR]", "Recovery:" or
    // "Fault:", and the shared ExceptionRecoveryContract would still pass on a layout that only contains them.
    [Fact]
    public void AFailurePrintsTagRuleMessageRecoveryAvailableAndFault_InThatOrder()
    {
        var failure = new MeshConfigurationException("The configuration key mesh.prot is not known.", "Correct the spelling.", "Or remove the key.")
        {
            AvailableOptions = ["mesh.port"],
        };

        string expected = string.Join(
            Environment.NewLine,
            "[MESH ERROR] MeshConfigurationException",
            new string('=', 70),
            "The configuration key mesh.prot is not known.",
            string.Empty,
            "Recovery:",
            "  -> Correct the spelling.",
            "  -> Or remove the key.",
            string.Empty,
            "Available:",
            "  * mesh.port",
            string.Empty,
            "Fault: Configuration (retrying unchanged cannot succeed)");

        Assert.Equal(expected, failure.ToString());
    }
}
