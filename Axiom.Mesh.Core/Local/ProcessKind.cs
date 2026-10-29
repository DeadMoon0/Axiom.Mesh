namespace Axiom.Mesh.Core;

/// <summary>How a local connection reached the local port (LOCAL-SPEC.md §2, §4.2).</summary>
public enum ProcessKind
{
    /// <summary>Not stated: the value of an uninitialised field, never adopted.</summary>
    Unknown = 0,

    /// <summary>A program on the pipe, whose process identity the Host read from the operating system (§2.2).</summary>
    Pipe = 1,

    /// <summary>An in-process service on its in-memory stream pair (§4.2, DESIGN.md §10.3).</summary>
    InProcess = 2,
}
