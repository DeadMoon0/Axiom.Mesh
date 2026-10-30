using System;
using System.Collections.Generic;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host;

/// <summary>Opens the platform's pipe listener (LOCAL-SPEC.md §2.1).</summary>
internal static class PipeListeners
{
    /// <summary>Opens the listener for a pipe location.</summary>
    /// <param name="location">Where the pipe is.</param>
    /// <param name="allowUsers">Users a system service admits besides its own account.</param>
    /// <param name="time">The daemon's clock.</param>
    /// <returns>The listener.</returns>
    /// <exception cref="HostException">The pipe is taken or cannot be created.</exception>
    public static IPipeListener Open(PipeLocation location, IReadOnlyList<string> allowUsers, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (OperatingSystem.IsWindows())
            return WindowsPipeListener.Open(location.PipeName, allowUsers, time);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            return UnixSocketListener.Open(location.Path);
        throw new HostException(
            HostExitCode.Environment,
            MeshFault.Environment,
            "This platform has no pipe the daemon can listen on (LOCAL-SPEC.md §2.1).",
            "Run the daemon on Windows or Linux; mobile platforms connect in process.");
    }
}
