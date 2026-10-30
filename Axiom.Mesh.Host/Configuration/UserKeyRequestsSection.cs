using System.Collections.Generic;

namespace Axiom.Mesh.Host;

/// <summary><c>userKeyRequests</c>: the requests that spend the user's private key (LOCAL-SPEC.md §2.2, §13).</summary>
internal sealed record UserKeyRequestsSection
{
    /// <summary>
    /// The executables (absolute paths) that may send them; empty lets every program of the user. Every such request is
    /// logged with its process id and executable either way. Default none.
    /// </summary>
    public List<string> ConfineTo { get; set; } = [];
}
