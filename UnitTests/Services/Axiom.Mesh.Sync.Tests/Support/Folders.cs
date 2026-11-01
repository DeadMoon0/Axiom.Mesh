using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync.Tests.Support;

/// <summary>A test's folders under one temporary root, deleted at the end, compared by their files' bytes with the reserved names left out.</summary>
internal sealed class Folders : IDisposable
{
    private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "axiom-mesh-sync-tests-" + Guid.NewGuid().ToString("N")[..12]);

    public Folders()
    {
        foreach (string side in new[] { "alice", "bob" })
            Directory.CreateDirectory(System.IO.Path.Combine(root, side));
    }

    public string Path(string relative) => System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Write(string relative, string text)
    {
        string path = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public string Read(string relative) => File.Exists(Path(relative)) ? File.ReadAllText(Path(relative)) : "(absent)";

    /// <summary>Whether two sides hold the same files with the same bytes and the same directories.</summary>
    public bool Same(string left, string right)
    {
        try
        {
            return Snapshot(left).SequenceEqual(Snapshot(right));
        }
        catch (IOException)
        {
            return false;
        }
    }

    public IReadOnlyList<string> Snapshot(string side)
    {
        string folder = Path(side);
        return [.. Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .Select(p => (Relative: System.IO.Path.GetRelativePath(folder, p).Replace('\\', '/'), Full: p))
            .Where(p => !p.Relative.Split('/').Any(n => n.StartsWith(FileNameRule.ReservedPrefix, StringComparison.Ordinal)))
            .OrderBy(p => p.Relative, StringComparer.Ordinal)
            .Select(p => Directory.Exists(p.Full) ? p.Relative + "/" : p.Relative + " " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p.Full))))];
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still held by a closing handle; the system's temporary folder is cleaned eventually.
        }
    }
}
