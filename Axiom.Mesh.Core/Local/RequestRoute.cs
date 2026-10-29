using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// One row of the dispatcher's table: what LOCAL-SPEC.md states about a request (its descriptor and roles, whether it
/// needs an identity, whether it spends the user key, whether it is served while draining) and the handler that serves
/// it on this Host; a row without a handler is answered <c>UnknownRequest</c>.
/// </summary>
/// <param name="Descriptor">The request as <see cref="RequestTable"/> describes it.</param>
/// <param name="Handler">The handler; <see langword="null"/> when this Host does not serve the request yet (D7).</param>
/// <param name="NeedsIdentity">The request is on the list of §6 that fails with <c>NoIdentity</c> before an identity exists.</param>
/// <param name="UserKey">A user-key request of §2.2: logged with the process identity, and confinable to listed executables.</param>
/// <param name="ServedWhileDraining">Served while Draining (§3.4): <c>Unsubscribe</c>, <c>CancelOperation</c>, <c>GetOperation</c>, <c>ListOperations</c>.</param>
/// <param name="Ordered">Applied in the order received on its connection, one at a time, behind the connection's other ordered requests (§3.4).</param>
internal sealed record RequestRoute(RequestDescriptor Descriptor, RequestHandler? Handler, bool NeedsIdentity, bool UserKey, bool ServedWhileDraining, bool Ordered);
