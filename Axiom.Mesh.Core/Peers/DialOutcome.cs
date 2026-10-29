namespace Axiom.Mesh.Core;

/// <summary>How a dial ended, as the dialler reports it to the peer table.</summary>
internal enum DialOutcome
{
    /// <summary>Not dialled after all: admission on this side refused the place, or the node stops. No backoff.</summary>
    Skipped = 0,

    /// <summary>The connection could not be opened (SPEC.md §4.4): back off.</summary>
    Failed = 1,

    /// <summary>The connection was served and has ended; whether it counts as a refusal depends on whether it established.</summary>
    Ended = 2,
}
