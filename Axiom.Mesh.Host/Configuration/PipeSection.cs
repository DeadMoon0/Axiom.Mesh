using System.Collections.Generic;

namespace Axiom.Mesh.Host;

/// <summary><c>pipe</c>: where clients reach the daemon and who may (LOCAL-SPEC.md §2).</summary>
internal sealed record PipeSection
{
    /// <summary>
    /// The pipe's name (Windows) or socket path (Linux); <see langword="null"/> for the platform's default.
    /// <c>AXIOM_MESH_PIPE</c> in the daemon's environment wins over it, as clients find the pipe by that variable.
    /// Default null.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Operating-system users a daemon that runs as a system service admits besides its own account (LOCAL-SPEC.md
    /// §2.2): <c>DOMAIN\name</c> on Windows, the uid on Linux. Ignored by a daemon that runs as a user. Default none.
    /// </summary>
    public List<string> AllowUsers { get; set; } = [];
}
