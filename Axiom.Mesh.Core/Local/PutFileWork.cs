using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The work of LOCAL-SPEC.md §9.5 <c>PutFile</c>: the file read once from start to end, cut into Chunks of exactly 1 MiB
/// except the last (SPEC.md §8.2), each stored and pinned with the service's pin as it goes, then the ChunkList; the result
/// names the ChunkList. A path that does not exist is <c>NotFound</c>, one that is no regular file <c>Invalid</c>, a file
/// that changes while it is read or the file system refusing <c>IoError</c>, a pin over the budget <c>StoreFull</c>. A
/// cancelled PutFile keeps the Chunks already stored, pinned, and no ChunkList (§8.5).
/// </summary>
internal sealed class PutFileWork
{
    private readonly MeshNode node;
    private readonly string path;
    private readonly PinReason reason;
    private readonly Hash32 source;

    /// <summary>The work for one file.</summary>
    /// <param name="node">The node.</param>
    /// <param name="path">The file, an absolute path.</param>
    /// <param name="reason">The pin's reason (Service in the service role).</param>
    /// <param name="source">The pin's source.</param>
    public PutFileWork(MeshNode node, string path, PinReason reason, Hash32 source)
    {
        this.node = node;
        this.path = path;
        this.reason = reason;
        this.source = source;
    }

    /// <summary>Runs the work.</summary>
    /// <param name="progress">Where progress is reported, in bytes.</param>
    /// <param name="cancellationToken">The operation's token.</param>
    /// <returns>The outcome.</returns>
    public async Task<OperationOutcome> RunAsync(OperationProgress progress, CancellationToken cancellationToken)
    {
        if (Directory.Exists(path))
            return OperationOutcome.Failed(Status.Invalid, $"{path} is a directory, not a regular file.");
        if (!File.Exists(path))
            return OperationOutcome.Failed(Status.NotFound, $"{path} does not exist.");

        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationOutcome.Failed(Status.IoError, ex.Message);
        }

        await using (file.ConfigureAwait(false))
        {
            ulong total = (ulong)file.Length;
            ulong done = 0;
            uint chunks = 0;
            bool full = false;
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ChunkList list;
            try
            {
                list = await ChunkStreamer.CutAsync(
                    file,
                    async (chunk, ct) =>
                    {
                        if (!await PutAsync(chunk, ct).ConfigureAwait(false))
                        {
                            full = true;
                            await stopping.CancelAsync().ConfigureAwait(false);
                            ct.ThrowIfCancellationRequested();
                        }

                        done += (ulong)chunk.Data.Length;
                        chunks++;
                        progress.Report(done, total, $"{chunks} chunks stored");
                    },
                    stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (full && !cancellationToken.IsCancellationRequested)
            {
                return StoreFull();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return OperationOutcome.Failed(Status.IoError, ex.Message);
            }

            if (!await PutAsync(list, cancellationToken).ConfigureAwait(false))
                return StoreFull();
            progress.Report(done, total, "stored");
            return OperationOutcome.Done(RecordBytes.Of(new PutFileResult { Hash = list.Hash, Size = list.Size, Chunks = (uint)list.Chunks.Count }));
        }
    }

    private static OperationOutcome StoreFull() =>
        OperationOutcome.Failed(Status.StoreFull, "The store's pinned bytes would exceed its budget; release pins or raise the budget.");

    // Stores the object and pins it for the service; false when the budget does not allow it.
    private async Task<bool> PutAsync(StorableObject storable, CancellationToken cancellationToken)
    {
        AdmitResult stored = await node.Store.PutAsync(storable, cancellationToken).ConfigureAwait(false);
        if (stored.Outcome == AdmitOutcome.StoreFull)
            return false;
        await node.Writer.SendAsync(new PinCommand(PinCommandKind.Pin, [storable.Hash], reason, source), cancellationToken).ConfigureAwait(false);
        return true;
    }
}
