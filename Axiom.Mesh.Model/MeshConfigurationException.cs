using System;

namespace Axiom.Mesh;

/// <summary>
/// The configuration a node or a client was given cannot run: a value out of range, a key nothing reads, a service
/// entry that contradicts another.
/// </summary>
/// <remarks>
/// Thrown before anything was started or opened, so nothing needs undoing. Retrying unchanged cannot succeed; the
/// configuration has to change first.
/// </remarks>
public sealed class MeshConfigurationException : MeshException
{
    /// <summary>Creates the exception for one problem and the steps out of it.</summary>
    /// <param name="friendlyMessage">What is wrong, naming the key or value, in one or two sentences.</param>
    /// <param name="firstStep">The first thing to do; taking it separately makes an exception without a step unrepresentable.</param>
    /// <param name="moreSteps">Further steps, in the order to try them.</param>
    public MeshConfigurationException(string friendlyMessage, string firstStep, params string[] moreSteps)
        : base(friendlyMessage, [firstStep, .. (moreSteps ?? throw new ArgumentNullException(nameof(moreSteps)))])
    {
    }

    /// <inheritdoc />
    public override MeshFault Fault => MeshFault.Configuration;

    /// <inheritdoc />
    public override bool IsTransient => false;
}
