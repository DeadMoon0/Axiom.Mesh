using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The long work of the namespace (CHAT-SERVICE.md §8, S-D8), each run as a job off the call that started it and reported
/// by <c>job</c> events: <c>attach</c> stores a file or a folder and remembers the objects it pinned; <c>get</c> fetches an
/// attachment from the peers and writes it to disk. A job never throws: what goes wrong ends it Failed with its status.
/// </summary>
internal static class ChatJobs
{
    /// <summary>Stores a file (a ChunkList) or a folder (a Tree), then records what it pinned (LOCAL-SPEC.md §9.5).</summary>
    /// <param name="session">The connection the job works on.</param>
    /// <param name="job">The started job.</param>
    /// <param name="kind">File or Folder.</param>
    /// <param name="path">The absolute path.</param>
    /// <param name="name">The name the attachment shows.</param>
    /// <returns>A task that completes when the job ended.</returns>
    public static async Task AttachAsync(ChatSession session, JobView job, AttachmentKind kind, string path, string name)
    {
        long startedMs = session.Time.GetUtcNow().ToUnixTimeMilliseconds();
        ulong total = kind == AttachmentKind.File ? (ulong)SizeOf(path) : 0;
        var progress = new JobProgress(session, job.Job, total);
        try
        {
            ObjectHash root;
            ulong size;
            IReadOnlyList<ObjectHash> objects;
            if (kind == AttachmentKind.Folder)
            {
                LocalResult<PublishedTree> published = await Trees.PublishDirectoryAsync(session.Client, path, progress, session.Ending).ConfigureAwait(false);
                if (!published.IsOk)
                {
                    await FailAsync(session, job, published.Status, published.Error?.Message ?? "The folder could not be stored.").ConfigureAwait(false);
                    return;
                }

                (root, size, objects) = (published.Value.Root, AttachmentRule.SizeOf(published.Value), published.Value.Objects);
            }
            else
            {
                LocalResult<StoredFile> stored = await StoreFiles.PutFileAsync(session.Client, path, progress, session.Ending).ConfigureAwait(false);
                if (!stored.IsOk)
                {
                    await FailAsync(session, job, stored.Status, stored.Error?.Message ?? "The file could not be stored.").ConfigureAwait(false);
                    return;
                }

                (root, size, objects) = (stored.Value.ChunkList, stored.Value.Size, [.. stored.Value.Chunks, stored.Value.ChunkList]);
            }

            await session.Index.AddPendingAsync(root, objects, startedMs, CancellationToken.None).ConfigureAwait(false);
            JobView done = session.Jobs.End(job.Job, j => j with { State = JobState.Done, Done = size, Total = size, Attachment = RowViews.Attachment(name, root, size) });
            await ReportAsync(session, done).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or LocalConnectionException or IOException or UnauthorizedAccessException)
        {
            await FailAsync(session, job, Status.IoError, ex.Message).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fetches an attachment recursively from the peers (LOCAL-SPEC.md §6.4 <c>Fetch</c>, its operation followed on topic
    /// <c>operations</c> subscribed first) and writes it: a ChunkList as a file, a Tree as a folder.
    /// </summary>
    /// <param name="session">The connection the job works on.</param>
    /// <param name="job">The started job.</param>
    /// <param name="package">The message's Channel.</param>
    /// <param name="attachment">The attachment.</param>
    /// <param name="path">The absolute path to write.</param>
    /// <param name="existing">What to do when the path exists.</param>
    /// <returns>A task that completes when the job ended.</returns>
    public static async Task GetAsync(ChatSession session, JobView job, PackageId package, Payloads.Attachment attachment, string path, ExistingPath existing)
    {
        try
        {
            LocalResult<bool> fetched = await FetchAsync(session, job, package, attachment.Hash).ConfigureAwait(false);
            if (!fetched.IsOk)
            {
                await FailAsync(session, job, fetched.Status, fetched.Error?.Message ?? "The attachment could not be fetched.").ConfigureAwait(false);
                return;
            }

            AttachmentKind kind = await KindOfAsync(session.Client, attachment.Hash, session.Ending).ConfigureAwait(false);
            Verdict verdict = AttachmentRule.ForGet(kind, existing, path);
            if (!verdict.IsPass)
            {
                await FailAsync(session, job, verdict.Status, verdict.Problem).ConfigureAwait(false);
                return;
            }

            var progress = new WriteProgress(session, job.Job);
            (Status status, Error? error, ulong bytes) = kind == AttachmentKind.File
                ? Outcome(await StoreFiles.MaterialiseFileAsync(session.Client, attachment.Hash, path, existing, progress, session.Ending).ConfigureAwait(false), f => f.Bytes)
                : Outcome(await StoreFiles.MaterialiseAsync(session.Client, attachment.Hash, path, existing, progress, session.Ending).ConfigureAwait(false), t => t.Bytes);
            if (status != Status.Ok)
            {
                await FailAsync(session, job, status, error?.Message ?? "The attachment could not be written.").ConfigureAwait(false);
                return;
            }

            JobView done = session.Jobs.End(job.Job, j => j with { State = JobState.Done, Done = bytes, Total = bytes });
            await ReportAsync(session, done).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or LocalConnectionException or IOException or UnauthorizedAccessException)
        {
            await FailAsync(session, job, Status.IoError, ex.Message).ConfigureAwait(false);
        }
    }

    /// <summary>Reports a job's state with a <c>job</c> event (zero Package).</summary>
    /// <param name="session">The connection.</param>
    /// <param name="view">The job.</param>
    /// <returns>A task that completes when the Host answered.</returns>
    public static Task ReportAsync(ChatSession session, JobView view) =>
        session.EmitAsync(ChatEvents.Job, default, new JobEvent { Job = view }, CancellationToken.None);

    private static async Task<LocalResult<bool>> FetchAsync(ChatSession session, JobView job, PackageId package, ObjectHash hash)
    {
        // LOCAL-SPEC.md §7.2: subscribe before the request, so the operation's end is never missed.
        LocalResult<Subscription<OperationChanged>> subscribed = await session.Client.SubscribeAsync<OperationChanged>(Filter.Any, session.Ending).ConfigureAwait(false);
        if (!subscribed.IsOk)
            return LocalResult<bool>.Failure(subscribed.Status, subscribed.Error ?? new Error());
        await using Subscription<OperationChanged> operations = subscribed.Value;
        LocalResult<uint> started = await session.Client.SendAsync(new Fetch { PackageId = package, Hash = hash, Recursive = true }, session.Ending).ConfigureAwait(false);
        if (!started.IsOk)
            return LocalResult<bool>.Failure(started.Status, started.Error ?? new Error());
        await foreach (OperationChanged changed in operations.WithCancellation(session.Ending).ConfigureAwait(false))
        {
            Operation operation = changed.Operation;
            if (operation.OperationId != started.Value)
                continue;
            switch (operation.State)
            {
                case OperationState.Running:
                    if (session.Jobs.Progress(job.Job, operation.Done, operation.Total) is { } reported)
                        await ReportAsync(session, reported).ConfigureAwait(false);
                    break;
                case OperationState.Done:
                    return LocalResult<bool>.Ok(true);
                default:
                    return LocalResult<bool>.Failure(operation.Status == Status.Ok ? Status.NotFound : operation.Status, ErrorText.Of(operation.Message.Length > 0 ? operation.Message : "No peer supplied the attachment.", "hash"));
            }
        }

        return LocalResult<bool>.Failure(Status.IoError, ErrorText.Of("The fetch's events ended before it did.", "hash"));
    }

    // What an attachment's object is, now that it is stored here.
    private static async Task<AttachmentKind> KindOfAsync(LocalClient client, ObjectHash hash, CancellationToken cancellationToken)
    {
        LocalResult<ReadOnlyMemory<byte>> read = await client.SendAsync(new GetObject { Hash = hash }, cancellationToken).ConfigureAwait(false);
        if (!read.IsOk || !HashedObject.TryVerify(read.Value.ToArray(), hash, out HashedObject verified) || !Objects.TryParse(verified, out ParsedObject? parsed, out _))
            return AttachmentKind.Unassigned;
        return parsed switch
        {
            ChunkList => AttachmentKind.File,
            Tree => AttachmentKind.Folder,
            _ => AttachmentKind.Unassigned,
        };
    }

    private static (Status Status, Error? Error, ulong Bytes) Outcome<T>(LocalResult<T> result, Func<T, ulong> bytes) =>
        result.IsOk ? (Status.Ok, null, bytes(result.Value)) : (result.Status, result.Error, 0);

    private static async Task FailAsync(ChatSession session, JobView job, Status status, string problem)
    {
        JobView failed = session.Jobs.End(job.Job, j => j with { State = JobState.Failed, Status = (ushort)status, Problem = Timeline.Preview(problem, ChatLimits.ProblemLength) });
        ChatLog.JobEnded(session.Service.Log, failed.Job, failed.Kind, failed.State, failed.Problem);
        await ReportAsync(session, failed).ConfigureAwait(false);
    }

    private static long SizeOf(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    // Bytes read while storing, reported coalesced (JobRule).
    private sealed class JobProgress(ChatSession session, uint job, ulong total) : IProgress<long>
    {
        public void Report(long value)
        {
            if (session.Jobs.Progress(job, (ulong)Math.Max(value, 0), total) is { } reported)
                _ = ReportAsync(session, reported);
        }
    }

    // Bytes written while materialising, reported coalesced (JobRule).
    private sealed class WriteProgress(ChatSession session, uint job) : IProgress<MaterialiseProgress>
    {
        public void Report(MaterialiseProgress value)
        {
            if (session.Jobs.Progress(job, value.Done, value.Total) is { } reported)
                _ = ReportAsync(session, reported);
        }
    }
}
