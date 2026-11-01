using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Service;

/// <summary>
/// One connection of a service to its Host, as the runtime hands it to the service's handlers: the client to send requests
/// on, what the Host said in its <c>HelloAck</c>, the catch-up plan, the state and the log. Made by the runtime for each
/// connection; the service never opens one itself.
/// </summary>
public sealed class ServiceSession
{
    internal ServiceSession(ServiceDefinition definition, LocalClient client, Hash32 storeEpoch, IndexCatchUp catchUp, ServiceState state, ILogger log)
    {
        Definition = definition;
        Client = client;
        StoreEpoch = storeEpoch;
        CatchUp = catchUp;
        State = state;
        Log = log;
    }

    /// <summary>Who the service is.</summary>
    public ServiceDefinition Definition { get; }

    /// <summary>The connection, in the service role: <c>WriteEntry</c>, <c>PutObject</c>, <c>ListHistory</c> and every request marked S.</summary>
    public LocalClient Client { get; }

    /// <summary>What the Host said: its version, flags, identity, store epoch and newest store sequence.</summary>
    public HelloAck Host => Client.Host;

    /// <summary>The store epoch this session indexes under (LOCAL-SPEC.md §9.3), from the last <c>RegisterKind</c>.</summary>
    public Hash32 StoreEpoch { get; }

    /// <summary>How this session catches up: whether the index must be discarded, and the store sequence it resumes at.</summary>
    public IndexCatchUp CatchUp { get; }

    /// <summary>The service's state: its database and the runtime's cursor.</summary>
    public ServiceState State { get; }

    /// <summary>The service's log; a spawned service's log reaches the Host's log.</summary>
    public ILogger Log { get; }

    /// <summary>Publishes an event in the service's namespace (LOCAL-SPEC.md §9.10) to every app subscribed to topic service.</summary>
    /// <param name="eventName">The event's name, the service's own.</param>
    /// <param name="package">The Package it concerns: one of the service's kinds, followed; zero for none.</param>
    /// <param name="payload">The event's payload, the service's own layout, at most 1 MiB.</param>
    /// <param name="cancellationToken">Stops waiting for the Host's answer.</param>
    /// <returns>Ok, or why the Host refused it (<c>NotFound</c>, <c>KindNotRegistered</c>, <c>Invalid</c>).</returns>
    public ValueTask<LocalResult<NoContent>> EmitAsync(string eventName, PackageId package, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        Client.SendAsync(new Emit { Event = eventName, PackageId = package, Payload = payload }, cancellationToken);

    /// <inheritdoc />
    public override string ToString() => $"session of {Definition.Name} on {Host.HostVersion}";
}
