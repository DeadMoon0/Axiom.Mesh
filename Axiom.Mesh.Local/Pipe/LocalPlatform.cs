namespace Axiom.Mesh.Local;

/// <summary>The platforms of LOCAL-SPEC.md §2.1, which decide what the pipe is and where it lives.</summary>
public enum LocalPlatform : byte
{
    /// <summary>Not one of the platforms below: there is no pipe.</summary>
    Unknown = 0,

    /// <summary>A named pipe, <c>\\.\pipe\axiom-mesh</c>.</summary>
    Windows = 1,

    /// <summary>A Unix domain socket, <c>$XDG_RUNTIME_DIR/axiom-mesh.sock</c>, else <c>/run/user/&lt;uid&gt;/axiom-mesh.sock</c>.</summary>
    Linux = 2,

    /// <summary>A Unix domain socket, <c>$TMPDIR/axiom-mesh.sock</c>.</summary>
    MacOS = 3,

    /// <summary>No pipe: an in-memory stream pair inside the app process.</summary>
    Android = 4,

    /// <summary>No pipe: an in-memory stream pair inside the app process.</summary>
    IOS = 5,
}
