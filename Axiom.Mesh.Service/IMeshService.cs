using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Service;

/// <summary>
/// A service on Axiom.Mesh (LOCAL-SPEC.md §9, DESIGN.md §10.3): the driver behind an app, with its namespace as its only
/// surface. The runtime (<see cref="ServiceProgram"/>) connects it, registers its kinds, catches it up and keeps it
/// connected; the service only answers what happens. Calls arrive while it catches up; every other handler runs one at a
/// time, in the order the Host delivered.
/// </summary>
/// <remarks>
/// A service owns no user interface (DESIGN.md §10.5): it writes nothing to the console, formats nothing for a human and
/// emits events and replies in its own namespace, which its <c>.Client</c> library types for every app.
/// </remarks>
public interface IMeshService
{
    /// <summary>Who the service is: its name, version and kinds.</summary>
    ServiceDefinition Definition { get; }

    /// <summary>
    /// The service is connected and its kinds are registered; before its first Entry. When
    /// <see cref="ServiceSession.CatchUp"/> says <c>DiscardIndex</c>, the Host's store epoch changed (LOCAL-SPEC.md §9.3):
    /// drop every row built from Entries before returning; the runtime then starts the cursor over.
    /// </summary>
    /// <param name="session">The connection, the Host, the catch-up plan and the state.</param>
    /// <param name="cancellationToken">Cancelled when the service stops.</param>
    /// <returns>A task that completes when the service is ready for Entries.</returns>
    Task OnConnectedAsync(ServiceSession session, CancellationToken cancellationToken);

    /// <summary>
    /// An Entry of one of the service's kinds was stored (LOCAL-SPEC.md §9.3): once per storing, in store order, from the
    /// catch-up and then live. After a crash the last one may come again, so dedupe by its hash; the runtime advances the
    /// cursor only after this returned.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="stored">The Entry, with its store sequence.</param>
    /// <param name="cancellationToken">Cancelled when the service stops.</param>
    /// <returns>A task that completes when the Entry is indexed.</returns>
    Task OnEntryAsync(ServiceSession session, EntryStored stored, CancellationToken cancellationToken);

    /// <summary>A Package of one of the service's kinds changed (LOCAL-SPEC.md §7.3): listed, followed, a Manifest, a Grant, a Head, dropped.</summary>
    /// <param name="session">The session.</param>
    /// <param name="changed">The change and the Package after it.</param>
    /// <param name="cancellationToken">Cancelled when the service stops.</param>
    /// <returns>A task that completes when the change is applied.</returns>
    Task OnPackageChangedAsync(ServiceSession session, PackageChanged changed, CancellationToken cancellationToken);

    /// <summary>
    /// A user client called a method of the service (LOCAL-SPEC.md §6.7, §9.8). Runs beside the other handlers, off the read
    /// loop; answer within the deadline. Long work is a job the reply names and events report (S-D8).
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="call">The caller's number and name, the method and the payload.</param>
    /// <param name="deadline">Cancelled at 60 seconds and when the connection ends.</param>
    /// <returns>The reply: a payload, or a status with its explanation.</returns>
    ValueTask<CallReply> OnCallAsync(ServiceSession session, ForwardedCall call, CancellationToken deadline);

    /// <summary>The connection ended; the runtime reconnects by <see cref="ReconnectRule"/>. Release what belongs to the connection.</summary>
    /// <param name="session">The session that ended.</param>
    /// <param name="cancellationToken">Not cancelled by the connection's end.</param>
    /// <returns>A task that completes when the service let go of the session.</returns>
    Task OnDisconnectedAsync(ServiceSession session, CancellationToken cancellationToken);
}
