namespace Axiom.Mesh.Service;

/// <summary>What a catch-up does next (<see cref="CatchUpStep"/>).</summary>
public enum CatchUpAction
{
    /// <summary>Not stated: the value of an uninitialised field; never a decided step.</summary>
    Unknown = 0,

    /// <summary>Read the next page with <c>ListStored</c>: more than 256 Entries may be missing.</summary>
    Page = 1,

    /// <summary>Subscribe to topic entries with <c>fromSeq</c>: the Host replays the rest and continues live.</summary>
    Subscribe = 2,
}
