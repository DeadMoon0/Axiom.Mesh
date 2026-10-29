using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// Serves one request of LOCAL-SPEC.md §6–§10 after the dispatcher's checks passed, and produces its one response. A
/// handler that changes state enqueues store-writer commands and answers once their change set committed, so that a
/// request that fails has no effect (§3.4).
/// </summary>
/// <param name="context">The connection and the node.</param>
/// <param name="correlation">The request's correlation.</param>
/// <param name="request">The request, of the type its route names.</param>
/// <param name="cancellationToken">Cancelled when the connection ends.</param>
/// <returns>The answer.</returns>
internal delegate ValueTask<LocalAnswer> RequestHandler(RequestContext context, uint correlation, LocalRequest request, CancellationToken cancellationToken);
