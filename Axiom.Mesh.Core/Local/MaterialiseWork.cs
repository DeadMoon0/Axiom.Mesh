using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The work of LOCAL-SPEC.md §6.4 <c>Materialise</c>, under SPEC.md §11.4 and §8.4: what is lacking is fetched first, as
/// <c>Fetch</c> does; then a Tree becomes a directory and a ChunkList a file. A file is written only when every chunk is
/// stored and the ChunkList's size holds, to a hidden temporary name beside it that is renamed into place, so a cancelled
/// or failed write leaves no partial file; executable mode is applied where the file system has one; a symlink is
/// written as a regular file holding its target. Names that would escape the directory, collide case-insensitively or
/// that the file system cannot hold are conflicts, counted and named in the message. A directory this Host materialised
/// holds a marker file, which <c>existing</c> 2 requires before it deletes one.
/// </summary>
internal sealed class MaterialiseWork
{
    /// <summary>The marker file in every directory this Host materialised (§6.4 <c>existing</c> 2).</summary>
    public const string Marker = ".axiom-mesh-materialised";

    private const string PartialPrefix = ".axiom-mesh-partial-";
    private readonly MeshNode node;
    private readonly Materialise request;
    private readonly List<string> conflictNames = [];
    private ulong total;
    private ulong done;
    private uint files;
    private OperationProgress? reporting;

    /// <summary>The work of one request.</summary>
    /// <param name="node">The node.</param>
    /// <param name="request">The request, checked already.</param>
    public MaterialiseWork(MeshNode node, Materialise request)
    {
        this.node = node;
        this.request = request;
    }

    /// <summary>Runs the work.</summary>
    /// <param name="progress">Where progress is reported, in bytes written.</param>
    /// <param name="cancellationToken">The operation's token.</param>
    /// <returns>The outcome.</returns>
    public async Task<OperationOutcome> RunAsync(OperationProgress progress, CancellationToken cancellationToken)
    {
        reporting = progress;
        var fetcher = new ObjectFetcher(node, request.PackageId, recursive: true, static (_, _, _) => Task.FromResult(true));
        if (await fetcher.RunAsync(request.Hash, progress, cancellationToken).ConfigureAwait(false) is { } failed)
            return failed;

        ParsedObject? root = await ReadAsync(request.Hash, cancellationToken).ConfigureAwait(false);
        if (root is not (Tree or ChunkList))
            return OperationOutcome.Failed(Status.Invalid, $"{request.Hash} is neither a Tree nor a ChunkList.");
        total = await SizeOfAsync(root, cancellationToken).ConfigureAwait(false);
        string target = Path.GetFullPath(request.Path);
        if (Prepare(target, root is Tree) is { } refused)
            return refused;

        try
        {
            if (root is Tree tree)
            {
                Directory.CreateDirectory(target);
                await File.WriteAllTextAsync(Path.Combine(target, Marker), request.Hash.ToString(), cancellationToken).ConfigureAwait(false);
                await WriteTreeAsync(tree, target, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await WriteFileAsync((ChunkList)root, target, executable: false, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationOutcome.Failed(Status.IoError, ex.Message);
        }

        progress.Report(done, total, conflictNames.Count == 0 ? $"{files} files written" : "conflicts: " + string.Join(", ", conflictNames));
        return OperationOutcome.Done(RecordBytes.Of(new MaterialiseResult { Files = files, Bytes = done, Conflicts = (uint)conflictNames.Count }));
    }

    // §6.4 existing: 1 fails when the path exists; 2 deletes a regular file or a directory this Host materialised, never
    // through a symlink or reparse point; 3 writes into what is there.
    private OperationOutcome? Prepare(string target, bool directory)
    {
        FileSystemInfo? existing = File.Exists(target) ? new FileInfo(target) : Directory.Exists(target) ? new DirectoryInfo(target) : null;
        if (existing is null)
            return null;
        if (request.Existing == ExistingPath.Fail)
            return OperationOutcome.Failed(Status.AlreadyExists, $"{target} exists.");
        if (existing.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return OperationOutcome.Failed(Status.Invalid, $"{target} is a symlink or reparse point; it is never written through.");
        if (request.Existing == ExistingPath.Merge)
            return existing is DirectoryInfo == directory ? null : OperationOutcome.Failed(Status.Invalid, $"{target} is a {(directory ? "file" : "directory")}; it cannot be written into.");

        if (existing is DirectoryInfo folder)
        {
            if (!File.Exists(Path.Combine(target, Marker)))
                return OperationOutcome.Failed(Status.Invalid, $"{target} is a directory this Host did not materialise; it is not deleted.");
            folder.Delete(recursive: true);
        }
        else
        {
            existing.Delete();
        }

        return null;
    }

    private async Task WriteTreeAsync(Tree tree, string directory, CancellationToken cancellationToken)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TreeEntry entry in tree.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(directory, entry.Name));
            if (!MaterialiseRule.MayWrite(entry.Name) || !taken.Add(entry.Name) || !string.Equals(Path.GetDirectoryName(target), directory, StringComparison.Ordinal) || IsLink(target))
            {
                conflictNames.Add(entry.Name);
                continue;
            }

            switch (entry.Mode)
            {
                case TreeEntryMode.Directory when await ReadAsync(entry.Hash, cancellationToken).ConfigureAwait(false) is Tree child:
                    Directory.CreateDirectory(target);
                    await WriteTreeAsync(child, target, cancellationToken).ConfigureAwait(false);
                    break;
                case TreeEntryMode.File or TreeEntryMode.ExecutableFile when await ReadAsync(entry.Hash, cancellationToken).ConfigureAwait(false) is ChunkList list:
                    if (!await WriteFileAsync(list, target, entry.Mode == TreeEntryMode.ExecutableFile, cancellationToken).ConfigureAwait(false))
                        conflictNames.Add(entry.Name);
                    break;
                case TreeEntryMode.Symlink when await ReadAsync(entry.Hash, cancellationToken).ConfigureAwait(false) is Chunk link:
                    await WriteBytesAsync(target, link.Data, cancellationToken).ConfigureAwait(false);
                    files++;
                    break;
                default:
                    conflictNames.Add(entry.Name);
                    break;
            }
        }
    }

    // SPEC.md §11.4: written only with every chunk stored and the size verified, to a temporary name renamed into place.
    private async Task<bool> WriteFileAsync(ChunkList list, string target, bool executable, CancellationToken cancellationToken)
    {
        string partial = Path.Combine(Path.GetDirectoryName(target)!, PartialPrefix + Path.GetFileName(target));
        ulong written = 0;
        try
        {
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 0))
            {
                foreach (ObjectHash hash in list.Chunks)
                {
                    if (await ReadAsync(hash, cancellationToken).ConfigureAwait(false) is not Chunk chunk)
                        return false;
                    await output.WriteAsync(chunk.Data, cancellationToken).ConfigureAwait(false);
                    written += (ulong)chunk.Data.Length;
                    done += (ulong)chunk.Data.Length;
                    reporting?.Report(done, total, $"writing {Path.GetFileName(target)}");
                }

                if (written != list.Size)
                    return false;
                output.Flush(flushToDisk: true);
            }

            if (executable && !OperatingSystem.IsWindows())
                File.SetUnixFileMode(partial, File.GetUnixFileMode(partial) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            File.Move(partial, target, overwrite: true);
            files++;
            return true;
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }

    private async Task WriteBytesAsync(string target, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        string partial = Path.Combine(Path.GetDirectoryName(target)!, PartialPrefix + Path.GetFileName(target));
        try
        {
            await File.WriteAllBytesAsync(partial, bytes.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(partial, target, overwrite: true);
            done += (ulong)bytes.Length;
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }

    private static bool IsLink(string path)
    {
        FileSystemInfo? existing = File.Exists(path) ? new FileInfo(path) : Directory.Exists(path) ? new DirectoryInfo(path) : null;
        return existing is not null && existing.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private async Task<ulong> SizeOfAsync(ParsedObject root, CancellationToken cancellationToken)
    {
        if (root is ChunkList list)
            return list.Size;
        ulong size = 0;
        var pending = new Stack<Tree>([(Tree)root]);
        while (pending.TryPop(out Tree? tree))
        {
            foreach (TreeEntry entry in tree.Entries)
            {
                if (entry.Mode == TreeEntryMode.Directory && await ReadAsync(entry.Hash, cancellationToken).ConfigureAwait(false) is Tree child)
                    pending.Push(child);
                else if (entry.Mode != TreeEntryMode.Directory)
                    size += entry.Size;
            }
        }

        return size;
    }

    private async Task<ParsedObject?> ReadAsync(ObjectHash hash, CancellationToken cancellationToken)
    {
        byte[]? bytes = await StoredObjects.ReadAsync(node.Files, hash, cancellationToken).ConfigureAwait(false);
        return bytes is not null && HashedObject.TryVerify(bytes, hash, out HashedObject verified) && Objects.TryParse(verified, out ParsedObject? parsed, out _)
            ? parsed
            : null;
    }
}
