using System;

namespace Axiom.Mesh.Service;

/// <summary>A request the runtime's loop needs (a page, a subscription) was refused: the session ends and the runtime reconnects.</summary>
internal sealed class RequestFailedException(string message) : Exception(message);
