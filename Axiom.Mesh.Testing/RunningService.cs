using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Testing;

/// <summary>
/// A service running in process beside a test node (<see cref="TestNode.RunServiceAsync(IMeshService, System.Threading.CancellationToken)"/>,
/// <see cref="IMeshFixture.RunServiceAsync(string, IMeshService, System.Threading.CancellationToken)"/>): the runtime of <see cref="ServiceProgram"/> over the node's local port, with
/// its state kept by the node across runs, as a service's state directory is kept across restarts. Disposing it stops it
/// as Ctrl-C would.
/// </summary>
public sealed class RunningService : IAsyncDisposable
{
    private readonly CancellationTokenSource stop;
    private readonly ObservedService observed;
    private int stopping;

    private RunningService(string name, ObservedService observed, CancellationTokenSource stop, Task<ServiceExitCode> exited)
    {
        Name = name;
        this.observed = observed;
        this.stop = stop;
        Exited = exited;
    }

    /// <summary>The service's name.</summary>
    public string Name { get; }

    /// <summary>Completes with the exit code when the service stopped: disposed, or refused for good.</summary>
    public Task<ServiceExitCode> Exited { get; }

    /// <summary>Waits until the service is connected and told so (its <c>OnConnectedAsync</c> returned) on a connection that lasts.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when it is connected.</returns>
    /// <exception cref="TestMeshException">It did not connect within <see cref="TestMesh.Deadline"/>, or it exited.</exception>
    public async Task WhenConnectedAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        Task first = await Task.WhenAny(observed.Connected, Exited).WaitAsync(linked.Token).ConfigureAwait(false);
        if (first == Exited)
        {
            throw new TestMeshException(
                $"The service {Name} exited with {await Exited.ConfigureAwait(false)} before it connected.",
                "Configure the service on the node with TestServices.With(...) in TestMeshOptions.Local, so that its name and token are known.");
        }
    }

    /// <summary>Stops the service and waits until it exited; a second call only waits.</summary>
    /// <returns>A task that completes when it exited.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref stopping, 1) == 0)
            await stop.CancelAsync().ConfigureAwait(false);
        await Exited.WaitAsync(TestMesh.Deadline).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override string ToString() => $"service {Name} ({(Exited.IsCompleted ? "exited" : "running")})";

    /// <summary>Starts a service in the background with any environment: over a real pipe, with a state directory on disk.</summary>
    /// <param name="service">The service.</param>
    /// <param name="environment">Its environment.</param>
    /// <returns>The running service.</returns>
    public static RunningService Start(IMeshService service, ServiceEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(environment);
        var observed = new ObservedService(service);
        var stop = new CancellationTokenSource();
        Task<ServiceExitCode> exited = Task.Run(() => ServiceProgram.RunAsync(observed, ["run"], System.IO.TextWriter.Null, System.IO.TextWriter.Null, environment, stop.Token), CancellationToken.None);
        return new RunningService(service.Definition.Name, observed, stop, exited);
    }
}
