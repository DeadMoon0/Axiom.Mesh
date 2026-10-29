using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Core.Storage;

/// <summary>
/// Object files in a store directory, layout 1 of LOCAL-SPEC.md §9.7: the object with hash <c>h</c> is
/// <c>&lt;store&gt;/objects/&lt;h[0..1]&gt;/&lt;h[2..3]&gt;/&lt;h&gt;</c>, holding exactly its bytes.
/// </summary>
/// <remarks>
/// <para>
/// A write goes to <c>&lt;h&gt;.tmp-&lt;random&gt;</c> in the object's own directory, is flushed to disk, and is renamed into place
/// without overwriting (SPEC.md §11.1), so a crash leaves the object absent or complete; a name that already exists is
/// a finished object, and the write is dropped. On Linux the directory is flushed too where the runtime lets a
/// directory be opened; on Windows a rename is durable with the file.
/// </para>
/// <para>
/// Readers open with read and delete sharing, so that eviction can unlink a file a reader holds; the reader keeps the
/// bytes. A temporary file left by a crash is never found under a hash and is harmless.
/// </para>
/// </remarks>
public sealed class DirectoryObjectFiles : IObjectFiles
{
    private const string ObjectsFolder = "objects";
    private const string TemporaryMarker = ".tmp-";
    private readonly IEntropy entropy;

    /// <summary>Object files under <paramref name="storePath"/>; the directory is created when it is first written to.</summary>
    /// <param name="storePath">The store directory, which holds the <c>objects</c> folder.</param>
    /// <param name="entropy">Randomness for the temporary names.</param>
    public DirectoryObjectFiles(string storePath, IEntropy entropy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        ArgumentNullException.ThrowIfNull(entropy);
        StorePath = Path.GetFullPath(storePath);
        this.entropy = entropy;
    }

    /// <inheritdoc />
    public string? StorePath { get; }

    /// <summary>The file an object is stored in, whether or not it exists.</summary>
    /// <param name="hash">The object's hash.</param>
    /// <returns>The absolute path.</returns>
    public string PathOf(ObjectHash hash)
    {
        string hex = hash.ToString();
        return Path.Combine(StorePath!, ObjectsFolder, hex[..2], hex[2..4], hex);
    }

    /// <inheritdoc />
    public async ValueTask<bool> WriteAtomicAsync(ObjectHash hash, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        string final = PathOf(hash);
        if (File.Exists(final))
            return false;

        string folder = Path.GetDirectoryName(final)!;
        Directory.CreateDirectory(folder);
        string temporary = final + TemporaryMarker + TemporarySuffix();
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 0,
                PreallocationSize = bytes.Length,
            };
            await using (var stream = new FileStream(temporary, options))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporary, final, overwrite: false);
            }
            catch (IOException) when (File.Exists(final))
            {
                DeleteQuietly(temporary); // another write finished first; the object is the same bytes
                return false;
            }

            FlushDirectory(folder);
            return true;
        }
        catch
        {
            DeleteQuietly(temporary);
            throw;
        }
    }

    /// <inheritdoc />
    public bool TryOpenRead(ObjectHash hash, [NotNullWhen(true)] out Stream? stream)
    {
        try
        {
            stream = new FileStream(PathOf(hash), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 1);
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            stream = null;
            return false;
        }
    }

    /// <inheritdoc />
    public ObjectDeletion TryDelete(ObjectHash hash)
    {
        string path = PathOf(hash);
        if (!File.Exists(path))
            return ObjectDeletion.Absent;

        try
        {
            File.Delete(path);
            return File.Exists(path) ? ObjectDeletion.InUse : ObjectDeletion.Deleted;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ObjectDeletion.InUse; // a reader opened it without delete sharing; LOCAL-SPEC.md §9.7: evict later
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temporary name is never found under a hash; a leftover is harmless.
        }
    }

    // The rename is durable only once the directory entry is: fsync the directory where the runtime can open one.
    // .NET refuses to open a directory as a file on some platforms; then the rename's durability rests on the file
    // system's ordering, which STRUCTURAL-DEBT.md records.
    private static void FlushDirectory(string folder)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            using var directory = new FileStream(folder, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0);
            directory.Flush(flushToDisk: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort; see above.
        }
    }

    private string TemporarySuffix()
    {
        Span<byte> random = stackalloc byte[8];
        entropy.Fill(random);
        return Convert.ToHexString(random).ToLowerInvariant();
    }
}
