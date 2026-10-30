namespace Axiom.Mesh.Host;

/// <summary>What <c>axiom-meshd</c> tells the shell that started it.</summary>
public enum HostExitCode
{
    /// <summary>The command did what it was asked; for <c>run</c>, the daemon stopped when told to.</summary>
    Ok = 0,

    /// <summary>The command line did not name a command the daemon knows; the usage was printed.</summary>
    Usage = 1,

    /// <summary>The configuration, the home or the keys are not usable as they stand; the message says what to change.</summary>
    Configuration = 2,

    /// <summary>Another Host already serves the pipe; it was left alone and this one did not start (LOCAL-SPEC.md §2.1).</summary>
    PipeTaken = 3,

    /// <summary>The environment refused what the daemon needs: the store, the database, the mesh port or the pipe.</summary>
    Environment = 4,
}
