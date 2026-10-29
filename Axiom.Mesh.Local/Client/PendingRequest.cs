using System;

namespace Axiom.Mesh.Local;

/// <summary>A request sent and not yet answered; the read loop completes it from its <see cref="Response"/>.</summary>
internal abstract class PendingRequest
{
    /// <summary>Completes the request from its response, reading the body before the next frame is read.</summary>
    /// <param name="response">The response.</param>
    /// <returns><see langword="false"/> when the body is not the request's layout: a Host defect (§11.3).</returns>
    public abstract bool Complete(Response response);

    /// <summary>Fails the request: the connection ended before the response.</summary>
    /// <param name="error">Why.</param>
    public abstract void Fail(Exception error);
}
