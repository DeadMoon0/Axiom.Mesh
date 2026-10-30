using System;
using System.Runtime.InteropServices;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host;

/// <summary>The platform the daemon runs on, read once from the process.</summary>
internal static class HostPlatform
{
    /// <summary>The platform.</summary>
    public static LocalPlatform Current =>
        OperatingSystem.IsWindows() ? LocalPlatform.Windows
        : OperatingSystem.IsMacOS() ? LocalPlatform.MacOS
        : OperatingSystem.IsLinux() ? LocalPlatform.Linux
        : LocalPlatform.Unknown;

    /// <summary>The process's uid on Unix; <see langword="null"/> on Windows.</summary>
    public static uint? UserId => OperatingSystem.IsWindows() ? null : GetUid();

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();
}
