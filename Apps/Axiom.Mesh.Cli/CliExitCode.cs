namespace Axiom.Mesh.Cli;

/// <summary>What <c>axiom-mesh</c> tells the shell that started it (fundamentals plan §2.7).</summary>
public enum CliExitCode
{
    /// <summary>The command did what it was asked.</summary>
    Ok = 0,

    /// <summary>The command line did not parse; the usage was printed.</summary>
    Usage = 1,

    /// <summary>The Host is not reachable: nothing answers at its pipe, or it closed the connection.</summary>
    HostNotReachable = 2,

    /// <summary>The Host refused the request; one line says why (LOCAL-SPEC.md §11.1).</summary>
    Refused = 3,

    /// <summary>The Host did not answer in time, or the command was interrupted before it did.</summary>
    Timeout = 4,
}
