namespace Axiom.Mesh.Host;

/// <summary>What <c>axiom-meshd</c> tells the shell that started it.</summary>
public enum HostExitCode
{
    /// <summary>The command did what it was asked.</summary>
    Ok = 0,

    /// <summary>The command line did not name a command the daemon knows; the usage was printed.</summary>
    Usage = 1,
}
