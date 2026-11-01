using System;

namespace Axiom.Mesh.Service;

/// <summary>
/// A service cannot run as configured: its definition is invalid, it finds no token, or the Host does not let it register
/// a kind it defines. Thrown before the service connects or while it registers; retrying unchanged cannot succeed.
/// </summary>
public sealed class ServiceConfigurationException : MeshException
{
    /// <summary>Creates the exception for one problem and the steps out of it.</summary>
    /// <param name="friendlyMessage">What is wrong, naming the service and the key or kind.</param>
    /// <param name="firstStep">The first thing to do.</param>
    /// <param name="moreSteps">Further steps, in the order to try them.</param>
    public ServiceConfigurationException(string friendlyMessage, string firstStep, params string[] moreSteps)
        : base(friendlyMessage, [firstStep, .. (moreSteps ?? throw new ArgumentNullException(nameof(moreSteps)))])
    {
    }

    /// <inheritdoc />
    public override MeshFault Fault => MeshFault.Configuration;

    /// <inheritdoc />
    public override bool IsTransient => false;
}
