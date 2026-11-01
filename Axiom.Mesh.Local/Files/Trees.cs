using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Local;

/// <summary>Directories as Trees (SPEC.md §8.4), for services and the command line, over a <see cref="LocalClient"/>.</summary>
public static class Trees
{
    /// <summary>
    /// Stores a directory: every regular file below it with <see cref="StoreFiles.PutFileAsync(LocalClient, string, int, System.IProgress{long}, System.Threading.CancellationToken)"/> (symbolic links and
    /// other special files are left out), then one Tree per directory, the deepest first, each put with
    /// <c>PutObject</c>. The result's root is what an Entry references; its objects carry this connection's pins until
    /// it releases them with <c>Unpin</c> after writing the Entry (LOCAL-SPEC.md §9.5).
    /// </summary>
    /// <param name="client">The connection; a service, since <c>PutObject</c> is a service request.</param>
    /// <param name="directory">The directory; relative to the current directory unless absolute.</param>
    /// <param name="progress">Bytes read so far over all files, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the work; what was put stays, pinned.</param>
    /// <returns>
    /// The published Tree, or why not: <c>NotFound</c> for no such directory, <c>Invalid</c> for a name a Tree may not
    /// hold, and every refusal of <see cref="StoreFiles.PutFileAsync(LocalClient, string, int, System.IProgress{long}, System.Threading.CancellationToken)"/>.
    /// </returns>
    public static async Task<LocalResult<PublishedTree>> PublishDirectoryAsync(LocalClient client, string directory, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        string folder = Path.GetFullPath(directory);
        if (!Directory.Exists(folder))
            return Refused(Status.NotFound, $"{folder} is not a directory.");

        IReadOnlyList<string> paths;
        try
        {
            paths = RegularFiles(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refused(Status.IoError, $"{folder} cannot be listed: {ex.Message}");
        }

        var files = new List<(string Path, StoredFile File)>(paths.Count);
        var objects = new SortedSet<ObjectHash>();
        long before = 0;
        foreach (string relative in paths)
        {
            long offset = before;
            IProgress<long>? within = progress is null ? null : new Progress(offset, progress);
            LocalResult<StoredFile> stored = await StoreFiles.PutFileAsync(client, Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)), within, cancellationToken).ConfigureAwait(false);
            if (!stored.IsOk)
                return LocalResult<PublishedTree>.Failure(stored.Status, stored.Error);
            files.Add((relative, stored.Value));
            objects.Add(stored.Value.ChunkList);
            objects.UnionWith(stored.Value.Chunks);
            before += (long)stored.Value.Size;
        }

        if (!FolderTrees.TryBuild(files, out IReadOnlyList<Tree> trees, out string? problem))
            return Refused(Status.Invalid, $"{folder} cannot be published as a Tree: {problem}");
        foreach (Tree tree in trees)
        {
            LocalResult<StoredObject> put = await client.SendAsync(new PutObject { Object = tree.Bytes }, cancellationToken).ConfigureAwait(false);
            if (!put.IsOk)
                return LocalResult<PublishedTree>.Failure(put.Status, put.Error);
            objects.Add(tree.Hash);
        }

        return LocalResult<PublishedTree>.Ok(new PublishedTree { Root = trees[^1].Hash, Files = files.Count, Bytes = (ulong)before, Objects = [.. objects] });
    }

    // The regular files below a directory, relative to it with '/', in ordinal order; links, special files and the names
    // the helpers and services keep for themselves (FileNameRule.ReservedPrefix) left out.
    private static IReadOnlyList<string> RegularFiles(string directory)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        return [.. Directory.EnumerateFiles(directory, "*", options)
            .Select(f => Path.GetRelativePath(directory, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(p => !p.Split('/').Any(FileNameRule.IsReserved))
            .Order(StringComparer.Ordinal)];
    }

    private static LocalResult<PublishedTree> Refused(Status status, string message) =>
        LocalResult<PublishedTree>.Failure(status, new Error { Message = message });

    // One file's progress, reported on as the bytes of every file before it plus its own.
    private sealed class Progress(long offset, IProgress<long> total) : IProgress<long>
    {
        public void Report(long value) => total.Report(offset + value);
    }
}
