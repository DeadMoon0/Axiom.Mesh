namespace Axiom.Mesh.Local;

/// <summary>
/// What of the process environment decides the pipe's name (LOCAL-SPEC.md §2.1). <see cref="LocalPipe.Locate"/> reads
/// it from the running process; tests and the Host state it.
/// </summary>
public sealed record PipeEnvironment
{
    /// <summary><c>AXIOM_MESH_PIPE</c>: when set and not empty, the pipe's name on every desktop platform.</summary>
    public string? Pipe { get; init; }

    /// <summary><c>XDG_RUNTIME_DIR</c>, which holds the socket on Linux.</summary>
    public string? XdgRuntimeDir { get; init; }

    /// <summary><c>TMPDIR</c>, which holds the socket on macOS.</summary>
    public string? TmpDir { get; init; }

    /// <summary>The process's user id on Linux, for <c>/run/user/&lt;uid&gt;</c> when <c>XDG_RUNTIME_DIR</c> is unset.</summary>
    public uint? UserId { get; init; }
}
