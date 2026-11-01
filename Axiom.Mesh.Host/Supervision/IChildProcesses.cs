namespace Axiom.Mesh.Host;

/// <summary>
/// Where the daemon's spawned services come from: the operating system's processes (<see cref="SystemChildProcesses"/>), or
/// a fake in a test that decides when a child exits and what it prints.
/// </summary>
internal interface IChildProcesses
{
    /// <summary>Whether an executable exists: an entry whose executable is absent is not supervised (S-D11).</summary>
    /// <param name="executable">The absolute path.</param>
    /// <returns><see langword="true"/> when it is there.</returns>
    bool Exists(string executable);

    /// <summary>Starts a child.</summary>
    /// <param name="start">How.</param>
    /// <returns>The running child.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">The operating system could not start it.</exception>
    /// <exception cref="System.IO.IOException">The operating system could not start it.</exception>
    IChildProcess Start(ChildStart start);
}
