using System;
using System.IO;

namespace Axiom.Mesh.Core.Tests.Support;

/// <summary>A fresh directory under the system's temporary folder, deleted with everything in it on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "axiom-mesh-core-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file a test still holds open; the system's temporary folder is cleaned eventually.
        }
    }
}
