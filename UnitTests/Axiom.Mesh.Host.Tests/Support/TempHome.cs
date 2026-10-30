using System;
using System.IO;

namespace Axiom.Mesh.Host.Tests.Support;

/// <summary>A fresh daemon home under the system's temporary folder, deleted with everything in it on dispose.</summary>
internal sealed class TempHome : IDisposable
{
    public TempHome() => Directory.CreateDirectory(Path);

    /// <summary>The directory.</summary>
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "axiom-mesh-host-tests", Guid.NewGuid().ToString("N"));

    /// <summary>The paths of a daemon that keeps everything here.</summary>
    public HostPaths Paths => HostPaths.Resolve(HostPlatform.Current, _ => null, Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still held open; the system's temporary folder is cleaned eventually.
        }
    }
}
