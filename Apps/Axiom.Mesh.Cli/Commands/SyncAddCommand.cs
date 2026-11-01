using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.Mesh.Sync;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh sync add &lt;dir&gt; (--package &lt;archive&gt; | --create &lt;name&gt;) [--label main] [--ignore &lt;pattern&gt;]...
/// [--symlinks asfile|ignore]</c>: keeps a folder in sync with an Archive through the sync service (SYNC-SERVICE.md §5.2). The
/// Archive is created, or followed when it is not yet, as the user; then the service binds the folder and queues its first run.
/// </summary>
/// <param name="Directory">The folder, absolute or relative to the current directory.</param>
/// <param name="Package">The Archive as typed (id, prefix or name), or <see langword="null"/> with <paramref name="Create"/>.</param>
/// <param name="Create">The name of a new Archive, or <see langword="null"/> with <paramref name="Package"/>.</param>
/// <param name="Label">The branch.</param>
/// <param name="Ignore">The names to leave alone.</param>
/// <param name="Symlinks">What to do with a symlink of the branch.</param>
internal sealed record SyncAddCommand(string Directory, string? Package, string? Create, string Label, IReadOnlyList<string> Ignore, SymlinkPolicy Symlinks) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        string folder = Path.GetFullPath(Path.Combine(context.Environment.CurrentDirectory, Directory));
        if (!System.IO.Directory.Exists(folder))
            throw CliFailure.Problem($"{folder} is not a directory; create it first");
        IReadOnlyList<Service> services = await context.SendAsync(new ListServices()).ConfigureAwait(false);
        if (!services.Any(s => s.Name == SyncProtocol.ServiceName && s.Connected))
            throw CliFailure.Problem(CommandContext.SyncUnavailable);

        PackageId archive;
        if (Create is not null)
        {
            archive = (await context.SendAsync(new CreatePackage { Kind = PackageKind.Archive, Name = Create }).ConfigureAwait(false)).PackageId;
        }
        else
        {
            archive = await IdResolver.PackageAsync(context, Package!).ConfigureAwait(false);
            Local.Package known = await context.SendAsync(new GetPackage { PackageId = archive }).ConfigureAwait(false);
            if (known.State != PackageState.Followed)
                await context.SendAsync(new FollowPackage { PackageId = archive }).ConfigureAwait(false);
        }

        BindReply bound = await context.SyncAsync(new BindRequest { Directory = folder, PackageId = archive, Label = Label, Ignore = Ignore, Symlinks = Symlinks }).ConfigureAwait(false);
        return Print(context, TextTable.Pairs(
        [
            ("Bound", bound.Binding.Directory),
            ("Archive", archive.Value.ToHex()),
            ("Branch", bound.Binding.Label),
            ("Next", "the first run is queued; 'axiom-mesh sync status' shows how it went"),
        ]));
    }
}
