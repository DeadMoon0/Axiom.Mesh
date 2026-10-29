namespace Axiom.Mesh.Local;

/// <summary>
/// A 65th request would be outstanding on one connection (LOCAL-SPEC.md §3.4, §12): the Host would close the connection
/// for it, so the client refuses to send it. Nothing was sent.
/// </summary>
public sealed class RequestLimitException : MeshException
{
    internal RequestLimitException()
        : base(
            $"{Limits.MaxOutstandingRequests} requests are outstanding on this connection; one more would break the local protocol's limit, so it was not sent.",
            ["Await the responses of earlier requests before sending more, for example by bounding the requests in flight with a SemaphoreSlim of 64.", "Open a second connection for work that runs in parallel."])
    {
    }

    /// <inheritdoc />
    public override MeshFault Fault => MeshFault.Caller;

    /// <inheritdoc />
    public override bool IsTransient => true;
}
