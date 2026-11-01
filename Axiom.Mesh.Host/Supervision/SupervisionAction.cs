namespace Axiom.Mesh.Host;

/// <summary>What the supervisor does after a spawned service exited (<see cref="SupervisionRule"/>).</summary>
internal enum SupervisionAction
{
    /// <summary>Not stated: the value of an uninitialised field; never a decision.</summary>
    Unknown = 0,

    /// <summary>Start the service again after the delay.</summary>
    Restart = 1,

    /// <summary>Leave it stopped until the daemon restarts, and say so in the log.</summary>
    GiveUp = 2,

    /// <summary>The daemon stops: the service stays stopped, and that is no failure.</summary>
    Stopped = 3,
}
