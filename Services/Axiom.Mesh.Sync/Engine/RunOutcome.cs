using System.Collections.Generic;

namespace Axiom.Mesh.Sync;

/// <summary>How a run ended (<see cref="SyncRun"/>).</summary>
/// <param name="State">The binding's state after it.</param>
/// <param name="Error">Why it is in error or waiting; empty otherwise.</param>
/// <param name="Plan">What it did, for the log.</param>
/// <param name="Deferred">The Version DAG was behind the Host: nothing was planned, the run comes again once the stream catches up.</param>
/// <param name="Busy">Files were changing: a run comes again once they had time to settle.</param>
/// <param name="LocalChanges">Changes in the directory the run did not publish.</param>
/// <param name="Files">The files of the directory after the run.</param>
/// <param name="Bytes">Their bytes.</param>
/// <param name="Conflicts">The conflicts it recorded, numbered.</param>
internal sealed record RunOutcome(BindingState State, string Error, string Plan, bool Deferred, bool Busy, uint LocalChanges, ulong Files, ulong Bytes, IReadOnlyList<ConflictRow> Conflicts)
{
    /// <summary>A run that could not plan because the Version DAG was behind the Host.</summary>
    public static RunOutcome Behind(BindingState state) => new(state, string.Empty, "behind the stream", true, false, 0, 0, 0, []);

    /// <summary>A run that ended in a state with a reason.</summary>
    public static RunOutcome Failed(BindingState state, string error) => new(state, error, "stopped", false, false, 0, 0, 0, []);
}
