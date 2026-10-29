namespace Axiom.Mesh.Local;

/// <summary>What carries the local protocol on a desktop platform (LOCAL-SPEC.md §2.1).</summary>
public enum PipeKind : byte
{
    /// <summary>No pipe.</summary>
    None = 0,

    /// <summary>A Windows named pipe, byte mode, duplex.</summary>
    NamedPipe = 1,

    /// <summary>A Unix domain socket, <c>SOCK_STREAM</c>.</summary>
    UnixSocket = 2,
}
