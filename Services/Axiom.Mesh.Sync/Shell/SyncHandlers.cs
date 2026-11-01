using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Sync;

/// <summary>
/// The methods of <c>axiom-mesh-sync/1</c> (SYNC-SERVICE.md §5) on one connection: each decodes its request, checks it against
/// the rules and the Host, and answers at once from the service's state; the runs a method starts report themselves with
/// <c>binding</c> events.
/// </summary>
internal sealed class SyncHandlers
{
    private const int UnpinBatch = 1_024;

    private readonly ServiceSession session;
    private readonly BindingContext context;
    private readonly BindingBook book;

    /// <summary>The handlers of a connection.</summary>
    /// <param name="session">The connection.</param>
    /// <param name="context">What the bindings share.</param>
    /// <param name="book">The bindings.</param>
    public SyncHandlers(ServiceSession session, BindingContext context, BindingBook book)
    {
        this.session = session;
        this.context = context;
        this.book = book;
    }

    /// <summary>Answers one call.</summary>
    /// <param name="call">The forwarded call.</param>
    /// <param name="deadline">Cancelled at 60 seconds and when the connection ends.</param>
    /// <returns>The reply.</returns>
    public async ValueTask<CallReply> CallAsync(ForwardedCall call, CancellationToken deadline)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call.Method switch
        {
            SyncMethods.Bindings => await RunAsync<BindingsRequest, BindingsReply>(call, BindingsAsync, deadline).ConfigureAwait(false),
            SyncMethods.Bind => await RunAsync<BindRequest, BindReply>(call, BindAsync, deadline).ConfigureAwait(false),
            SyncMethods.Unbind => await RunAsync<UnbindRequest, UnbindReply>(call, UnbindAsync, deadline).ConfigureAwait(false),
            SyncMethods.Status => await RunAsync<StatusRequest, StatusReply>(call, StatusAsync, deadline).ConfigureAwait(false),
            SyncMethods.Scan => await RunAsync<ScanRequest, ScanReply>(call, ScanAsync, deadline).ConfigureAwait(false),
            SyncMethods.Conflicts => await RunAsync<ConflictsRequest, ConflictsReply>(call, ConflictsAsync, deadline).ConfigureAwait(false),
            _ => Refusal.Of(SyncStatus.UnknownMethod, $"The sync service has no method '{call.Method}' in {SyncProtocol.Namespace}.", "method"),
        };
    }

    /// <inheritdoc />
    public override string ToString() => "sync handlers of " + session;

    /// <summary>A directory as the bindings record it: absolute, without a trailing separator.</summary>
    /// <param name="directory">The directory as given.</param>
    /// <returns>The normalised directory, or <see langword="null"/> when it is not given absolute.</returns>
    public static string? Normal(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Path.IsPathFullyQualified(directory))
            return null;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }

    private static async Task<CallReply> RunAsync<TRequest, TReply>(ForwardedCall call, Func<TRequest, CancellationToken, Task<SyncAnswer<TReply>>> handle, CancellationToken deadline)
        where TRequest : class, ISyncMethod<TRequest, TReply>
        where TReply : class, IWireRecord<TReply>
    {
        if (!SyncCodec.TryDecode(call.Payload, out TRequest? request))
            return Refusal.Of(Status.Invalid, $"The payload is no {typeof(TRequest).Name} of {SyncProtocol.Namespace}.", "payload");
        SyncAnswer<TReply> answer = await handle(request, deadline).ConfigureAwait(false);
        return answer.Reply is { } reply ? CallReply.Ok(SyncCodec.Encode(reply)) : answer.Refusal!;
    }

    private Task<SyncAnswer<BindingsReply>> BindingsAsync(BindingsRequest request, CancellationToken deadline) =>
        Task.FromResult<SyncAnswer<BindingsReply>>(new BindingsReply { Bindings = book.Views() });

    private async Task<SyncAnswer<BindReply>> BindAsync(BindRequest request, CancellationToken deadline)
    {
        string? directory = Normal(request.Directory);
        if (directory is null || !Directory.Exists(directory))
            return Refusal.Of(SyncStatus.NotADirectory, $"'{request.Directory}' is not an existing directory given absolute.", "directory");
        switch (BindingRule.Check(book.Directories, directory, OperatingSystem.IsWindows()))
        {
            case BindingVerdict.AlreadyBound:
                return Refusal.Of(SyncStatus.AlreadyBound, $"{directory} is kept in sync already.", "directory");
            case BindingVerdict.Nested:
                return Refusal.Of(SyncStatus.NestedBinding, $"{directory} lies inside a directory kept in sync, or holds one.", "directory");
        }

        if (book.Count >= SyncLimits.Bindings)
            return Refusal.Of(Status.Busy, $"The sync service keeps {SyncLimits.Bindings} bindings at most; remove one first.", "directory");
        if (request.Label.Length == 0)
            return Refusal.Of(Status.Invalid, "A branch needs a name (PAYLOADS.md §2).", "label");
        if (request.Symlinks is not (SymlinkPolicy.AsFile or SymlinkPolicy.Ignore))
            return Refusal.Of(Status.Invalid, "Symlinks are written as files or not at all.", "symlinks");
        if (request.Ignore.Any(p => p.Contains('\n', StringComparison.Ordinal)))
            return Refusal.Of(Status.Invalid, "An ignore pattern holds a line break.", "ignore");
        if (!IgnoreRule.TryParse(request.Ignore, out _, out string? problem))
            return Refusal.Of(Status.Invalid, problem!, "ignore");

        LocalResult<Package> package = await session.Client.SendAsync(new GetPackage { PackageId = request.PackageId }, deadline).ConfigureAwait(false);
        if (!package.IsOk || package.Value.State != PackageState.Followed)
            return Refusal.Of(SyncStatus.NotFollowed, "The Archive is not followed on this Host; follow it first.", "packageId");
        if (package.Value.Kind != PackageKind.Archive)
            return Refusal.Of(SyncStatus.NotAnArchive, "The Package is not an Archive.", "packageId");

        var row = new BindingRow
        {
            Id = BindingIdentity.Of(directory, request.PackageId),
            Directory = directory,
            Package = RowBytes.Of(request.PackageId.Value),
            Label = request.Label,
            Symlinks = (byte)request.Symlinks,
            Ignore = string.Join('\n', request.Ignore),
            State = (byte)BindingState.InSync,
        };
        try
        {
            await MarkerFile.WriteAsync(directory, row.Id, request.PackageId, deadline).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refusal.Of(Status.IoError, $"The marker file cannot be written into {directory}: {ex.Message}", "directory");
        }

        await context.Store.SaveBindingAsync(row, CancellationToken.None).ConfigureAwait(false);
        var worker = new BindingWorker(context, BindingSpec.Of(row), row, 0);
        book.Add(worker);
        worker.Trigger();
        return new BindReply { Binding = worker.View() };
    }

    private async Task<SyncAnswer<UnbindReply>> UnbindAsync(UnbindRequest request, CancellationToken deadline)
    {
        if (Normal(request.Directory) is not { } directory || book.Exactly(directory) is not { } worker)
            return Refusal.Of(SyncStatus.NotBound, $"No binding holds '{request.Directory}'.", "directory");
        Binding last = worker.View();
        await book.RemoveAsync(worker).ConfigureAwait(false);
        MarkerFile.Delete(worker.Spec.Directory);
        IReadOnlyList<ObjectHash> held = await context.Store.PutsAsync(worker.Spec.Id, CancellationToken.None).ConfigureAwait(false);
        foreach (ObjectHash[] batch in held.Chunk(UnpinBatch))
            await session.Client.SendAsync(new Unpin { Hashes = batch }, CancellationToken.None).ConfigureAwait(false);
        await context.Store.DeleteBindingAsync(worker.Spec.Id, CancellationToken.None).ConfigureAwait(false);
        return new UnbindReply { Binding = last with { Pending = false } };
    }

    private Task<SyncAnswer<StatusReply>> StatusAsync(StatusRequest request, CancellationToken deadline)
    {
        if (request.Directory.Length == 0)
            return Task.FromResult<SyncAnswer<StatusReply>>(new StatusReply { Bindings = book.Views() });
        if (Normal(request.Directory) is not { } path || book.Holding(path) is not { } worker)
            return Task.FromResult<SyncAnswer<StatusReply>>(Refusal.Of(SyncStatus.NotBound, $"No binding holds '{request.Directory}'.", "directory"));
        return Task.FromResult<SyncAnswer<StatusReply>>(new StatusReply { Bindings = [worker.View()] });
    }

    private Task<SyncAnswer<ScanReply>> ScanAsync(ScanRequest request, CancellationToken deadline)
    {
        if (request.Directory.Length == 0)
            return Task.FromResult<SyncAnswer<ScanReply>>(new ScanReply { Queued = (ushort)book.TriggerAll() });
        if (Normal(request.Directory) is not { } path || book.Holding(path) is not { } worker)
            return Task.FromResult<SyncAnswer<ScanReply>>(Refusal.Of(SyncStatus.NotBound, $"No binding holds '{request.Directory}'.", "directory"));
        worker.Trigger();
        return Task.FromResult<SyncAnswer<ScanReply>>(new ScanReply { Queued = 1 });
    }

    private async Task<SyncAnswer<ConflictsReply>> ConflictsAsync(ConflictsRequest request, CancellationToken deadline)
    {
        BindingWorker? only = null;
        if (request.Directory.Length > 0 && (Normal(request.Directory) is not { } path || (only = book.Holding(path)) is null))
            return Refusal.Of(SyncStatus.NotBound, $"No binding holds '{request.Directory}'.", "directory");
        (List<ConflictRow> rows, bool more) = await context.Store.ConflictsAsync(only?.Spec.Id, request.After, request.Limit, deadline).ConfigureAwait(false);
        var page = new List<Conflict>(rows.Count);
        int budget = SyncLimits.PayloadBytes - 64;
        foreach (ConflictRow row in rows)
        {
            Conflict view = ConflictViews.Of(row, DirectoryOf(row.Binding) ?? string.Empty);
            budget -= SyncCodec.SizeOf(view);
            if (budget < 0)
            {
                more = true;
                break;
            }

            page.Add(view);
        }

        return new ConflictsReply { Conflicts = page, More = more };
    }

    private string? DirectoryOf(byte[] binding) =>
        book.Directories.FirstOrDefault(d => book.Exactly(d)?.Spec.Id.AsSpan().SequenceEqual(binding) == true);
}
