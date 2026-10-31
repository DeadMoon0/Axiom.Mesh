using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Payloads;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh package materialise &lt;package&gt; &lt;dir&gt; [--existing fail|replace|merge]</c>: writes the latest
/// Version of a followed Archive into a folder: what is missing is fetched from the peers that hold it (<c>Fetch</c>,
/// LOCAL-SPEC.md §6.4), then the Tree is written here, on this side of the pipe
/// (<see cref="StoreFiles.MaterialiseAsync"/>, SPEC.md §11.4).
/// </summary>
/// <param name="Package">The Archive as typed: id, prefix or name.</param>
/// <param name="Directory">The folder, absolute or relative to the current directory.</param>
/// <param name="Existing">What to do when the folder exists.</param>
internal sealed record PackageMaterialiseCommand(string Package, string Directory, ExistingPath Existing) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        string folder = Path.GetFullPath(Path.Combine(context.Environment.CurrentDirectory, Directory));
        PackageId package = await IdResolver.PackageAsync(context, Package).ConfigureAwait(false);
        EntryPage latest = await context.SendAsync(new ListHistory { PackageId = package, Reverse = true, Kinds = [EntryKind.Version], Limit = 1 }).ConfigureAwait(false);
        if (latest.Entries.Count == 0 || !EntryPayload.TryParse(EntryKind.Version, latest.Entries[0].Payload, out EntryPayload? payload) || payload is not VersionPayload version)
            throw CliFailure.Problem("the Package holds no Version here yet; follow it and wait until 'axiom-mesh history' shows one");

        LocalClient user = await context.UserAsync().ConfigureAwait(false);
        await OperationRunner.RunAsync<Fetch, FetchResult>(context, user, new Fetch { PackageId = package, Hash = version.Tree, Recursive = true, Pin = false }, "fetching").ConfigureAwait(false);

        IProgress<MaterialiseProgress>? progress = context.Environment.ErrorIsTerminal ? new Progress(context) : null;
        MaterialisedTree result = context.Ok(await StoreFiles.MaterialiseAsync(user, latest.Entries[0].Hash, folder, Existing, progress, context.Cancellation).ConfigureAwait(false));
        var lines = new List<(string, string)>
        {
            ("Materialised", $"{result.Files} files, {Formats.Bytes(result.Bytes)}, version {version.Label}"),
            ("Into", folder),
        };
        if (result.Conflicts.Count != 0)
            lines.Add(("Conflicts", $"{result.Conflicts.Count} ({string.Join(", ", result.Conflicts)}): names this file system cannot hold, or files that differ from their listing"));
        return Print(context, TextTable.Pairs(lines));
    }

    private sealed class Progress(CommandContext context) : IProgress<MaterialiseProgress>
    {
        public void Report(MaterialiseProgress value) =>
            context.Error.Write($"\rmaterialising: {Formats.Bytes(value.Done)}/{Formats.Bytes(value.Total)} {value.File}");
    }
}
