using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh package publish &lt;dir&gt; --package &lt;package&gt; [--label &lt;branch&gt;]</c>: publishes a folder as a
/// Version of an Archive (LOCAL-SPEC.md §9.5): as the external service <c>cli</c>, every file is stored with
/// <c>PutFile</c>, the folder's Trees with <c>PutObject</c>, a Version naming the root Tree is written, and the service's
/// pins are released, so the Outbound and policy pins decide from then on.
/// </summary>
/// <param name="Directory">The folder, absolute or relative to the current directory.</param>
/// <param name="Package">The Archive as typed: id, prefix or name.</param>
/// <param name="Label">The Version's branch name; <c>main</c> by default (SPEC.md §8.5).</param>
internal sealed record PackagePublishCommand(string Directory, string Package, string Label) : Command
{
    private const int UnpinBatch = 1_024;

    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        string folder = Path.GetFullPath(Path.Combine(context.Environment.CurrentDirectory, Directory));
        IReadOnlyList<string> paths = List(context, folder);
        PackageId package = await IdResolver.PackageAsync(context, Package).ConfigureAwait(false);
        LocalClient service = await context.ServiceAsync().ConfigureAwait(false);
        await context.SendAsServiceAsync(new RegisterKind { Kind = PackageKind.Archive, EntryKinds = [EntryKind.Version] }).ConfigureAwait(false);

        var files = new List<FolderTree.PublishedFile>();
        foreach (string relative in paths)
        {
            string path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
            PutFileResult stored = await OperationRunner.RunAsync<PutFile, PutFileResult>(context, service, new PutFile { Path = path }, "storing " + relative).ConfigureAwait(false);
            files.Add(new FolderTree.PublishedFile(relative, stored.Hash, stored.Size));
        }

        if (!FolderTree.TryBuild(files, out IReadOnlyList<Tree> trees, out string? problem))
            throw CliFailure.Problem($"{folder} cannot be published: {problem}");
        foreach (Tree tree in trees)
            await context.SendAsServiceAsync(new PutObject { Object = tree.Bytes }).ConfigureAwait(false);

        Tree root = trees[^1];
        var version = new VersionPayload { Tree = root.Hash, Label = Label, Prerelease = string.Empty, Notes = string.Empty };
        Entry entry = await context.SendAsServiceAsync(new WriteEntry { PackageId = package, Kind = EntryKind.Version, Payload = PayloadEncoder.Encode(version) }).ConfigureAwait(false);
        await UnpinAsync(context, files, trees).ConfigureAwait(false);

        ulong bytes = files.Aggregate(0ul, (sum, f) => sum + f.Size);
        return Print(context, TextTable.Pairs(
        [
            ("Published", $"{files.Count} files, {Formats.Bytes(bytes)}, as version {Label}"),
            ("Entry", entry.Hash.Value.ToHex()),
            ("Tree", root.Hash.Value.ToHex()),
        ]));
    }

    private static IReadOnlyList<string> List(CommandContext context, string folder)
    {
        IReadOnlyList<string> paths;
        try
        {
            paths = context.Environment.ListFiles(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw CliFailure.Problem($"{folder} cannot be read: {ex.Message}");
        }

        return paths.Count == 0 ? throw CliFailure.Problem($"{folder} holds no file to publish") : paths;
    }

    // LOCAL-SPEC.md §9.5: put, write the Entry, then unpin: the ChunkLists, their Chunks and the Trees.
    private static async Task UnpinAsync(CommandContext context, IReadOnlyList<FolderTree.PublishedFile> files, IReadOnlyList<Tree> trees)
    {
        var hashes = new SortedSet<ObjectHash>(trees.Select(t => t.Hash));
        foreach (FolderTree.PublishedFile file in files)
        {
            hashes.Add(file.ChunkList);
            ReadOnlyMemory<byte> bytes = await context.SendAsServiceAsync(new GetObject { Hash = file.ChunkList }).ConfigureAwait(false);
            if (Objects.TryParse(HashedObject.Of(bytes), out ParsedObject? parsed, out _) && parsed is ChunkList list)
            {
                foreach (ObjectHash chunk in list.Chunks)
                    hashes.Add(chunk);
            }
        }

        foreach (ObjectHash[] batch in hashes.Chunk(UnpinBatch))
            await context.SendAsServiceAsync(new Unpin { Hashes = batch }).ConfigureAwait(false);
    }
}
