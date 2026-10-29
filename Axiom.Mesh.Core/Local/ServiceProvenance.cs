namespace Axiom.Mesh.Core;

/// <summary>How a configured service proves its name (LOCAL-SPEC.md §4.2 check 3, §5.12).</summary>
public enum ServiceProvenance
{
    /// <summary>Not stated: the value of an uninitialised field; a service with it is refused.</summary>
    Unknown = 0,

    /// <summary>Compiled into the Host and connected over its in-memory pair; never granted on the pipe.</summary>
    InProcess = 1,

    /// <summary>A program the Host spawns, presenting the token the Host put into its environment.</summary>
    Spawned = 2,

    /// <summary>A program the user starts (the command line, a script), presenting the token the configuration holds.</summary>
    External = 3,
}
