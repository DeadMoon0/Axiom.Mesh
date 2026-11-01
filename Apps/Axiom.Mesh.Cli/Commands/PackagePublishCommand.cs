using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh package publish &lt;dir&gt; --package &lt;package&gt; [--label &lt;branch&gt;]</c>: publishes a folder as a
/// Version (PAYLOADS.md §2) of an Archive as the external service <c>cli</c> (LOCAL-SPEC.md §9.5): the folder is cut into
/// Chunks, ChunkLists and Trees here and put with <c>PutObject</c> (<see cref="Trees.PublishDirectoryAsync"/>), a Version
/// is written whose envelope references the root Tree, and the service's pins are released with <c>Unpin</c>, so the
/// Outbound and policy pins decide from then on.
/// </summary>
/// <param name="Directory">The folder, absolute or relative to the current directory.</param>
/// <param name="Package">The Archive as typed: id, prefix or name.</param>
/// <param name="Label">The Version's branch name; <c>main</c> by default (PAYLOADS.md §2).</param>
internal sealed record PackagePublishCommand(string Directory, string Package, string Label) : Command
{
    private const int UnpinBatch = 1_024;

    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        string folder = Path.GetFullPath(Path.Combine(context.Environment.CurrentDirectory, Directory));
        PackageId package = await IdResolver.PackageAsync(context, Package).ConfigureAwait(false);
        LocalClient service = await context.ServiceAsync().ConfigureAwait(false);
        LocalResult<KindRegistration> registered = await context.TrySendAsync(service, new RegisterKind { Kind = PackageKind.Archive, EntryKinds = [EntryKind.Version] }).ConfigureAwait(false);

        // S-D5: publishing stays direct; while the sync service runs it holds Archives, and the folder is its to publish.
        if (registered.Status == Status.KindTaken)
            throw CliFailure.Problem("the sync service holds Archives on this Host; use 'axiom-mesh sync add <dir> --package <archive>' to keep the folder in sync instead");
        context.Ok(registered);

        IProgress<long>? progress = context.Environment.ErrorIsTerminal ? new BytesProgress(context, "storing") : null;
        PublishedTree published = context.Ok(await Trees.PublishDirectoryAsync(service, folder, progress, context.Cancellation).ConfigureAwait(false));
        if (published.Files == 0)
            throw CliFailure.Problem($"{folder} holds no file to publish");

        var version = new VersionPayload { Tree = published.Root, Label = Label, Prerelease = string.Empty, Notes = string.Empty };
        Entry entry = await context.SendAsServiceAsync(WriteEntry.Of(package, version.ToBody())).ConfigureAwait(false);

        // LOCAL-SPEC.md §9.5: put, write the Entry, then unpin; the Outbound pins keep what no peer has yet.
        foreach (ObjectHash[] batch in published.Objects.Chunk(UnpinBatch))
            await context.SendAsServiceAsync(new Unpin { Hashes = batch }).ConfigureAwait(false);

        return Print(context, TextTable.Pairs(
        [
            ("Published", $"{published.Files} files, {Formats.Bytes(published.Bytes)}, as version {Label}"),
            ("Entry", entry.Hash.Value.ToHex()),
            ("Tree", published.Root.Value.ToHex()),
        ]));
    }
}
