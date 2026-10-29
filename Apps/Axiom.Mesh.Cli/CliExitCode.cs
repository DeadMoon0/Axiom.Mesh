namespace Axiom.Mesh.Cli;

/// <summary>What <c>axiom-mesh</c> tells the shell that started it.</summary>
public enum CliExitCode
{
    /// <summary>The command did what it was asked.</summary>
    Ok = 0,

    /// <summary>The command line did not parse; the usage was printed.</summary>
    Usage = 1,
}
