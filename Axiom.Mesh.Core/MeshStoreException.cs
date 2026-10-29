using System;

namespace Axiom.Mesh.Core;

/// <summary>
/// The node's store or database cannot be used as it is: a database written by another schema version, an index that
/// names an object whose file is gone, a store directory that cannot be written.
/// </summary>
/// <remarks>
/// Thrown when the store is opened, before anything was changed. Retrying unchanged cannot succeed: the store, the
/// database or the version of the software has to change first.
/// </remarks>
public sealed class MeshStoreException : MeshException
{
    /// <summary>Creates the exception for one problem and the steps out of it.</summary>
    /// <param name="friendlyMessage">What is wrong, naming the store or database, in one or two sentences.</param>
    /// <param name="firstStep">The first thing to do; taking it separately makes an exception without a step unrepresentable.</param>
    /// <param name="moreSteps">Further steps, in the order to try them.</param>
    public MeshStoreException(string friendlyMessage, string firstStep, params string[] moreSteps)
        : base(friendlyMessage, [firstStep, .. (moreSteps ?? throw new ArgumentNullException(nameof(moreSteps)))])
    {
    }

    /// <summary>Creates the exception for one problem, the steps out of it and the failure underneath.</summary>
    /// <param name="friendlyMessage">What is wrong, naming the store or database, in one or two sentences.</param>
    /// <param name="innerException">The failure underneath.</param>
    /// <param name="firstStep">The first thing to do.</param>
    /// <param name="moreSteps">Further steps, in the order to try them.</param>
    public MeshStoreException(string friendlyMessage, Exception innerException, string firstStep, params string[] moreSteps)
        : base(friendlyMessage, [firstStep, .. (moreSteps ?? throw new ArgumentNullException(nameof(moreSteps)))], innerException)
    {
    }

    /// <inheritdoc />
    public override MeshFault Fault => MeshFault.Environment;

    /// <inheritdoc />
    public override bool IsTransient => false;
}
