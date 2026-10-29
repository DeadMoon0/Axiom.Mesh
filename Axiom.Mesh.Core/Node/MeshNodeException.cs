using System;

namespace Axiom.Mesh.Core;

/// <summary>
/// A node cannot start or cannot do what it was asked as it stands: its key store and its database name different
/// devices, an identity is adopted from a Roster that does not list this device, or a call came at the wrong time.
/// </summary>
/// <remarks>Thrown before anything changed. Retrying unchanged cannot succeed.</remarks>
public sealed class MeshNodeException : MeshException
{
    /// <summary>Creates the exception for one problem, who must act, and the steps out of it.</summary>
    /// <param name="fault">Who must change something.</param>
    /// <param name="friendlyMessage">What is wrong, in one or two sentences.</param>
    /// <param name="firstStep">The first thing to do.</param>
    /// <param name="moreSteps">Further steps, in the order to try them.</param>
    public MeshNodeException(MeshFault fault, string friendlyMessage, string firstStep, params string[] moreSteps)
        : base(friendlyMessage, [firstStep, .. (moreSteps ?? throw new ArgumentNullException(nameof(moreSteps)))])
    {
        Fault = fault;
    }

    /// <inheritdoc />
    public override MeshFault Fault { get; }

    /// <inheritdoc />
    public override bool IsTransient => false;
}
