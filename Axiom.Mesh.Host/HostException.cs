using System;

namespace Axiom.Mesh.Host;

/// <summary>
/// The daemon cannot start or go on as its environment stands: another Host serves the pipe, the key directory is
/// readable by others, a key cannot be unwrapped. Says who must act and what to do; never a stack trace for the user.
/// </summary>
internal sealed class HostException : MeshException
{
    /// <summary>Creates the exception for one problem, who must act, and the steps out of it.</summary>
    /// <param name="exit">What the process exits with.</param>
    /// <param name="fault">Who must change something.</param>
    /// <param name="friendlyMessage">What is wrong, in one or two sentences.</param>
    /// <param name="firstStep">The first thing to do.</param>
    /// <param name="moreSteps">Further steps, in the order to try them.</param>
    public HostException(HostExitCode exit, MeshFault fault, string friendlyMessage, string firstStep, params string[] moreSteps)
        : base(friendlyMessage, [firstStep, .. (moreSteps ?? throw new ArgumentNullException(nameof(moreSteps)))])
    {
        Exit = exit;
        Fault = fault;
    }

    /// <summary>What the process exits with.</summary>
    public HostExitCode Exit { get; }

    /// <inheritdoc />
    public override MeshFault Fault { get; }

    /// <inheritdoc />
    public override bool IsTransient => false;
}
