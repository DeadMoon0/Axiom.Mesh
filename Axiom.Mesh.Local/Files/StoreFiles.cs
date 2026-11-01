using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Local;

/// <summary>
/// Files in and out of the store, for services and the command line, over a <see cref="LocalClient"/>: the Host stores
/// objects and knows nothing of files (LOCAL-SPEC.md §9.5, decision 5), so cutting a file into Chunks and writing a Tree
/// back to disk happen here, on the client's side, as the client's own user.
/// </summary>
public static class StoreFiles
{
    /// <summary>The marker file in every directory <see cref="MaterialiseAsync"/> wrote; <see cref="ExistingPath.Replace"/> requires it before it deletes one.</summary>
    public const string MarkerName = ".axiom-mesh-materialised";

    /// <summary>The prefix of the temporary name a file is written to before it is renamed into place.</summary>
    public const string PartialPrefix = ".axiom-mesh-partial-";

    /// <summary>
    /// How many Chunks <see cref="PutFileAsync(LocalClient, string, IProgress{long}, CancellationToken)"/> keeps in flight:
    /// eight 1 MiB buffers, well inside the 64 requests a connection may have outstanding (LOCAL-SPEC.md §3.4).
    /// </summary>
    public const int DefaultChunksInFlight = 8;

    /// <summary>
    /// Stores a file with <see cref="DefaultChunksInFlight"/> Chunks in flight; see
    /// <see cref="PutFileAsync(LocalClient, string, int, IProgress{long}, CancellationToken)"/>.
    /// </summary>
    /// <param name="client">The connection; a service, since <c>PutObject</c> is a service request.</param>
    /// <param name="path">The file.</param>
    /// <param name="progress">Bytes read so far, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels reading; the Chunks already put stay, pinned, and no ChunkList is put.</param>
    /// <returns>The stored file, or why not.</returns>
    public static Task<LocalResult<StoredFile>> PutFileAsync(LocalClient client, string path, IProgress<long>? progress, CancellationToken cancellationToken) =>
        PutFileAsync(client, path, DefaultChunksInFlight, progress, cancellationToken);

    /// <summary>
    /// Stores a file: reads it once from start to end, cuts it into Chunks of exactly 1 MiB except the last (SPEC.md
    /// §8.2), puts each with <c>PutObject</c>, then its ChunkList (§8.3). Up to <paramref name="chunksInFlight"/> puts are
    /// outstanding at once, each in a buffer of its own that is reused once the Host answered, so memory stays at that many
    /// Chunks whatever the file's size; the ChunkList names the Chunks in file order whatever order the Host answers in.
    /// Every object put carries this connection's pin (reason <c>Service</c> for a service) until it is released
    /// (LOCAL-SPEC.md §9.5). A zero-length file has no Chunks.
    /// </summary>
    /// <param name="client">The connection; a service, since <c>PutObject</c> is a service request.</param>
    /// <param name="path">The file.</param>
    /// <param name="chunksInFlight">1 to 64 Chunks outstanding at once; leave room for the connection's other requests.</param>
    /// <param name="progress">Bytes read so far, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">
    /// Cancels reading and waiting; the Chunks already put stay, pinned, and no ChunkList is put. A put already written is
    /// still applied and answered by the Host; its answer is discarded.
    /// </param>
    /// <returns>
    /// The stored file, or why not: <c>NotFound</c> for no such path, <c>Invalid</c> for a directory or a file larger than
    /// a ChunkList can name, <c>IoError</c> when the file system refuses or the file changes while it is read, and the
    /// first refusal of <c>PutObject</c> in file order, such as <c>StoreFull</c>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunksInFlight"/> is not 1 to 64.</exception>
    public static async Task<LocalResult<StoredFile>> PutFileAsync(LocalClient client, string path, int chunksInFlight, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunksInFlight, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunksInFlight, Limits.MaxOutstandingRequests);
        if (Directory.Exists(path))
            return Refused<StoredFile>(Status.Invalid, $"{path} is a directory, not a regular file.");

        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0, useAsync: true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Refused<StoredFile>(Status.NotFound, $"{path} does not exist.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refused<StoredFile>(Status.IoError, $"{path} cannot be read: {ex.Message}");
        }

        await using (file.ConfigureAwait(false))
        {
            try
            {
                return await PutAsync(client, file, path, chunksInFlight, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Refused<StoredFile>(Status.IoError, $"{path} cannot be read: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Writes a Tree into a directory (SPEC.md §8.4, §11.4): the Tree itself, or the Tree a <c>Version</c> Entry names
    /// (PAYLOADS.md §2). Objects are read straight from the store directory when this connection is a service the Host
    /// lets read it (<c>HelloAck</c> capability bit 0, LOCAL-SPEC.md §9.7), else with <c>GetObject</c>; every object must
    /// be stored here already (fetch it first with <c>Fetch</c>). A file is written to a hidden temporary name and
    /// renamed into place once complete; the directory gets the marker file <see cref="MarkerName"/>. Names this file
    /// system cannot hold, case-insensitive collisions and files whose size does not hold are conflicts, not written.
    /// </summary>
    /// <param name="client">The connection.</param>
    /// <param name="versionOrTree">A Tree, or a <c>Version</c> Entry.</param>
    /// <param name="directory">The directory to write into; relative to the current directory unless absolute.</param>
    /// <param name="existing">What to do when the directory exists.</param>
    /// <param name="progress">Bytes written so far, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels writing; the files already renamed into place stay, no partial file is left.</param>
    /// <returns>
    /// What was written, or why not: <c>NotFound</c> for an object not stored here, <c>Invalid</c> for a hash that is
    /// neither a Tree nor a Version, or a directory <paramref name="existing"/> does not allow writing to,
    /// <c>AlreadyExists</c> under <see cref="ExistingPath.Fail"/>, <c>IoError</c> when the file system refuses.
    /// </returns>
    public static async Task<LocalResult<MaterialisedTree>> MaterialiseAsync(LocalClient client, ObjectHash versionOrTree, string directory, ExistingPath existing, IProgress<MaterialiseProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ObjectReader reader = await ObjectReader.OpenAsync(client, cancellationToken).ConfigureAwait(false);
        LocalResult<Tree> root = await RootAsync(reader, versionOrTree, cancellationToken).ConfigureAwait(false);
        if (!root.IsOk)
            return LocalResult<MaterialisedTree>.Failure(root.Status, root.Error);
        var trees = new Dictionary<ObjectHash, Tree>();
        LocalResult<bool> loaded = await LoadTreesAsync(reader, root.Value, trees, cancellationToken).ConfigureAwait(false);
        if (!loaded.IsOk)
            return LocalResult<MaterialisedTree>.Failure(loaded.Status, loaded.Error);

        MaterialisePlan plan = MaterialisePlan.Of(root.Value, trees, OperatingSystem.IsWindows());
        string target = Path.GetFullPath(directory);
        ExistingDecision decision = ExistingRule.Decide(existing, StateOf(target));
        if (decision.Action == ExistingAction.Refuse)
            return Refused<MaterialisedTree>(decision.Status, $"{target}: {decision.Problem}");

        try
        {
            if (decision.Action == ExistingAction.ReplaceDirectory)
                Directory.Delete(target, recursive: true);
            else if (decision.Action == ExistingAction.ReplaceFile)
                File.Delete(target);
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, MarkerName), root.Value.Hash.ToString(), cancellationToken).ConfigureAwait(false);
            return await new TreeWriter(reader, plan, progress).WriteAsync(plan, target, root.Value.Hash, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refused<MaterialisedTree>(Status.IoError, $"{target} cannot be written: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes one file from its ChunkList (SPEC.md §8.3, §11.4), the single-file counterpart of
    /// <see cref="MaterialiseAsync"/>: the Chunks in order into a hidden temporary name beside <paramref name="path"/>
    /// (<see cref="PartialPrefix"/>), renamed into place only once every Chunk was written and their lengths add up to the
    /// ChunkList's size, so a cancelled or failed write leaves nothing under the name. Objects are read as
    /// <see cref="MaterialiseAsync"/> reads them; every one must be stored here already (fetch it first).
    /// </summary>
    /// <param name="client">The connection.</param>
    /// <param name="chunkList">The file's ChunkList.</param>
    /// <param name="path">The file to write; relative to the current directory unless absolute. Its directory must exist.</param>
    /// <param name="existing">
    /// What to do when something is at <paramref name="path"/>: <see cref="ExistingPath.Fail"/> or
    /// <see cref="ExistingPath.Replace"/> (a regular file only); <see cref="ExistingPath.Merge"/> is <c>Invalid</c> for a file.
    /// </param>
    /// <param name="progress">Bytes written so far, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels writing; no partial file is left.</param>
    /// <returns>
    /// What was written, or why not: <c>NotFound</c> for an object not stored here, <c>Invalid</c> for a hash that is no
    /// ChunkList, Chunks that do not add up to its size, an <paramref name="existing"/> that does not allow the write or a
    /// directory that does not exist, <c>AlreadyExists</c> under <see cref="ExistingPath.Fail"/>, <c>IoError</c> when the
    /// file system refuses.
    /// </returns>
    public static async Task<LocalResult<MaterialisedFile>> MaterialiseFileAsync(LocalClient client, ObjectHash chunkList, string path, ExistingPath existing, IProgress<MaterialiseProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(path);
        string target = Path.GetFullPath(path);
        ExistingDecision decision = ExistingRule.DecideFile(existing, StateOf(target));
        if (decision.Action == ExistingAction.Refuse)
            return Refused<MaterialisedFile>(decision.Status, $"{target}: {decision.Problem}");
        if (!Directory.Exists(Path.GetDirectoryName(target)))
            return Refused<MaterialisedFile>(Status.Invalid, $"{Path.GetDirectoryName(target)} does not exist; the file's directory must.");

        ObjectReader reader = await ObjectReader.OpenAsync(client, cancellationToken).ConfigureAwait(false);
        LocalResult<ParsedObject> named = await reader.ReadAsync(chunkList, cancellationToken).ConfigureAwait(false);
        if (!named.IsOk)
            return LocalResult<MaterialisedFile>.Failure(named.Status, named.Error);
        if (named.Value is not ChunkList list)
            return Refused<MaterialisedFile>(Status.Invalid, $"{chunkList} is not a ChunkList.");

        ulong done = 0;
        string name = Path.GetFileName(target);
        var copier = new ChunkCopier(reader, (bytes, file) =>
        {
            done += bytes;
            progress?.Report(new MaterialiseProgress(done, list.Size, file));
        });
        string partial = ChunkCopier.PartialOf(target);
        try
        {
            LocalResult<bool> copied = await copier.CopyAsync(list, partial, name, cancellationToken).ConfigureAwait(false);
            if (!copied.IsOk)
                return LocalResult<MaterialisedFile>.Failure(copied.Status, copied.Error);
            if (!copied.Value)
                return Refused<MaterialisedFile>(Status.Invalid, $"The Chunks of {chunkList} do not add up to the {list.Size} bytes it states (SPEC.md §11.4); nothing was written.");
            File.Move(partial, target, overwrite: decision.Action == ExistingAction.ReplaceFile);
            return LocalResult<MaterialisedFile>.Ok(new MaterialisedFile { ChunkList = chunkList, Bytes = list.Size });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refused<MaterialisedFile>(Status.IoError, $"{target} cannot be written: {ex.Message}");
        }
        finally
        {
            ChunkCopier.Discard(partial);
        }
    }

    /// <summary>
    /// Writes one file of a <see cref="MaterialisePlan"/> into a directory (SPEC.md §11.4), the one-file step of
    /// <see cref="MaterialiseAsync"/> for a service that decides file by file what to write (the sync service): the object
    /// is read as <see cref="ObjectReader"/> reads it and checked against the file's kind; a file's Chunks are written to a
    /// hidden temporary name beside it (<see cref="PartialPrefix"/>), every Chunk held and the ChunkList's size verified,
    /// marked executable where the file system has the bit, and renamed into place; a symlink is written as a regular file
    /// holding its target text. The file's directory must exist; a cancelled or failed write leaves nothing under its name.
    /// </summary>
    /// <param name="reader">Where objects are read (<see cref="ObjectReader.OpenAsync"/>).</param>
    /// <param name="file">The file: its path relative to <paramref name="directory"/>, its object, its kind.</param>
    /// <param name="directory">The directory the path is relative to, absolute.</param>
    /// <param name="cancellationToken">Cancels writing; no partial file is left.</param>
    /// <returns>
    /// The bytes written, or why not: <c>NotFound</c> for an object not stored here (fetch it first), <c>Invalid</c> when the
    /// object is not what the file's kind says, its Chunks do not add up to its size, or a directory or a link is at the
    /// path (nothing written: a conflict), <c>IoError</c> when the file system refuses.
    /// </returns>
    public static async Task<LocalResult<ulong>> WriteFileAsync(ObjectReader reader, PlannedFile file, string directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrEmpty(file.Path);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        string destination = Path.Combine(directory, file.Path.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            return await FileWriter.WriteAsync(reader, file, destination, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refused<ulong>(Status.IoError, $"{destination} cannot be written: {ex.Message}");
        }
    }

    private static async Task<LocalResult<StoredFile>> PutAsync(LocalClient client, FileStream file, string path, int chunksInFlight, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        long length = file.Length;
        DateTime changed = File.GetLastWriteTimeUtc(path);
        var chunks = new List<ObjectHash>();
        long size = 0;
        await using (var window = new ChunkWindow(client, chunksInFlight))
        {
            while (true)
            {
                if (await window.NextBufferAsync().ConfigureAwait(false) is not { } buffer)
                    return window.Failure!.Value;
                int read = await file.ReadAtLeastAsync(buffer.AsMemory(Chunker.HeaderLength, Limits.ChunkLength), Limits.ChunkLength, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    window.Return(buffer);
                    break;
                }

                if (chunks.Count == Limits.MaxChunkListChunks)
                {
                    window.Return(buffer);
                    return Refused<StoredFile>(Status.Invalid, $"{path} is larger than a ChunkList can name ({Limits.MaxChunkListChunks} Chunks of {Limits.ChunkLength} bytes, SPEC.md §8.3).");
                }

                chunks.Add(window.Put(buffer, Chunker.HeaderLength + read, cancellationToken));
                size += read;
                progress?.Report(size);
                if (read < Limits.ChunkLength)
                    break;
            }

            if (await window.CompleteAsync().ConfigureAwait(false) is { } failed)
                return failed;
        }

        if (size != length || file.Length != length || File.GetLastWriteTimeUtc(path) != changed)
            return Refused<StoredFile>(Status.IoError, $"{path} changed while it was read; store it again once it is complete.");

        ChunkList list = ObjectBuilders.ChunkList((ulong)size, chunks);
        LocalResult<StoredObject> listed = await client.SendAsync(new PutObject { Object = list.Bytes }, cancellationToken).ConfigureAwait(false);
        return listed.IsOk
            ? LocalResult<StoredFile>.Ok(new StoredFile { ChunkList = list.Hash, Size = list.Size, Chunks = chunks })
            : LocalResult<StoredFile>.Failure(listed.Status, listed.Error);
    }

    // The Tree to write: the hash itself, or the Tree a Version names.
    private static async Task<LocalResult<Tree>> RootAsync(ObjectReader reader, ObjectHash hash, CancellationToken cancellationToken)
    {
        LocalResult<ParsedObject> read = await reader.ReadAsync(hash, cancellationToken).ConfigureAwait(false);
        if (!read.IsOk)
            return LocalResult<Tree>.Failure(read.Status, read.Error);
        if (read.Value is Tree tree)
            return LocalResult<Tree>.Ok(tree);
        if (read.Value is UnverifiedEntry entry && EntryPayload.TryParse(entry.Content.Kind, entry.Content.Payload, out EntryPayload? payload) && payload is VersionPayload version)
        {
            LocalResult<ParsedObject> named = await reader.ReadAsync(version.Tree, cancellationToken).ConfigureAwait(false);
            if (!named.IsOk)
                return LocalResult<Tree>.Failure(named.Status, named.Error);
            if (named.Value is Tree versionTree)
                return LocalResult<Tree>.Ok(versionTree);
        }

        return Refused<Tree>(Status.Invalid, $"{hash} is neither a Tree nor a Version naming one.");
    }

    // Every Tree below the root, read before anything is written, so a missing one fails the work before it starts.
    private static async Task<LocalResult<bool>> LoadTreesAsync(ObjectReader reader, Tree root, Dictionary<ObjectHash, Tree> trees, CancellationToken cancellationToken)
    {
        var pending = new Stack<Tree>([root]);
        while (pending.TryPop(out Tree? tree))
        {
            foreach (TreeEntry entry in tree.Entries)
            {
                if (entry.Mode != TreeEntryMode.Directory || trees.ContainsKey(entry.Hash))
                    continue;
                LocalResult<ParsedObject> read = await reader.ReadAsync(entry.Hash, cancellationToken).ConfigureAwait(false);
                if (!read.IsOk)
                    return LocalResult<bool>.Failure(read.Status, read.Error);
                if (read.Value is Tree child)
                {
                    trees[entry.Hash] = child;
                    pending.Push(child);
                }
            }
        }

        return LocalResult<bool>.Ok(true);
    }

    private static PathState StateOf(string path)
    {
        FileSystemInfo? existing = File.Exists(path) ? new FileInfo(path) : Directory.Exists(path) ? new DirectoryInfo(path) : null;
        return existing switch
        {
            null => PathState.Absent,
            _ when existing.Attributes.HasFlag(FileAttributes.ReparsePoint) => PathState.Link,
            FileInfo => PathState.File,
            _ => File.Exists(Path.Combine(path, MarkerName)) ? PathState.MarkedDirectory : PathState.Directory,
        };
    }

    private static LocalResult<T> Refused<T>(Status status, string message) => LocalResult<T>.Failure(status, new Error { Message = message });
}
