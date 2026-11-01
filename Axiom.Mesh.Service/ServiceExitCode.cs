namespace Axiom.Mesh.Service;

/// <summary>
/// What a service process exits with. The Host's supervisor reads it: <see cref="Configuration"/> and
/// <see cref="Refused"/> are not retried until the Host restarts, every other exit is restarted with backoff.
/// </summary>
public enum ServiceExitCode
{
    /// <summary>Stopped when asked to, or printed what was asked.</summary>
    Ok = 0,

    /// <summary>The command line was not understood; the usage went to standard error.</summary>
    Usage = 1,

    /// <summary>The definition or the configuration cannot run: no token, a bad name, a kind the Host does not let it register.</summary>
    Configuration = 2,

    /// <summary>The Host refused the service's name or token for good (<c>Bye(Refused)</c>, LOCAL-SPEC.md §4.2).</summary>
    Refused = 3,

    /// <summary>The service's state could not be opened or written.</summary>
    State = 4,
}
