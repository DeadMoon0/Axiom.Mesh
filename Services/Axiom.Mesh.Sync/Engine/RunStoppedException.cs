using System;

namespace Axiom.Mesh.Sync;

/// <summary>
/// A test stopped a run where a crash would (<see cref="SyncServiceOptions.CrashAt"/>): the worker stops as the process would
/// have, and the next service started on the same state finds what the run left.
/// </summary>
internal sealed class RunStoppedException : Exception
{
    /// <summary>A stop at a step.</summary>
    /// <param name="step">Where the run stopped.</param>
    public RunStoppedException(RunStep step)
        : base($"The test stopped the run at {step}.") => Step = step;

    /// <summary>A stop.</summary>
    public RunStoppedException()
        : base("The test stopped the run.")
    {
    }

    /// <summary>A stop with a message.</summary>
    /// <param name="message">Why.</param>
    public RunStoppedException(string message)
        : base(message)
    {
    }

    /// <summary>A stop with a message and a cause.</summary>
    /// <param name="message">Why.</param>
    /// <param name="innerException">The cause.</param>
    public RunStoppedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Where the run stopped.</summary>
    public RunStep Step { get; }
}
