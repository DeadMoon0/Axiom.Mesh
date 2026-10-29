using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Core;

/// <summary>The node's log messages, source-generated.</summary>
internal static partial class NodeLog
{
    /// <summary>One fact about a connection or a peer.</summary>
    /// <param name="logger">The node's logger.</param>
    /// <param name="level">The fact's level.</param>
    /// <param name="fact">The fact.</param>
    [LoggerMessage(EventId = 1, Message = "{Fact}")]
    public static partial void Fact(ILogger logger, LogLevel level, NodeFact fact);
}
