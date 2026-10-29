using System;

namespace Axiom.Mesh.Testing;

/// <summary>
/// What a test mesh or a scripted peer was asked to wait for did not happen within its deadline, or happened otherwise:
/// a node that did not connect, a message of another type, bytes where a refusal was expected.
/// </summary>
/// <remarks>The test's expectation failed; the message says what was expected and what the mesh did instead.</remarks>
public sealed class TestMeshException : MeshException
{
    /// <summary>Creates the exception for one failed expectation.</summary>
    /// <param name="friendlyMessage">What was expected and what happened.</param>
    /// <param name="firstStep">Where to look first.</param>
    /// <param name="moreSteps">Further steps.</param>
    public TestMeshException(string friendlyMessage, string firstStep, params string[] moreSteps)
        : base(friendlyMessage, [firstStep, .. (moreSteps ?? throw new ArgumentNullException(nameof(moreSteps)))])
    {
    }

    /// <inheritdoc />
    public override MeshFault Fault => MeshFault.Caller;

    /// <inheritdoc />
    public override bool IsTransient => false;
}
