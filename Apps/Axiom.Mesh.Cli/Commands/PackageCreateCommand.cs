using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh package create &lt;name&gt; [--kind archive|channel] [--retention ...]</c>: a new Package owned by this user (LOCAL-SPEC.md §6.4).</summary>
/// <param name="Name">Its name.</param>
/// <param name="Kind">Archive (published folders) or Channel (messages).</param>
/// <param name="Retention">What other devices keep of it by default.</param>
internal sealed record PackageCreateCommand(string Name, PackageKind Kind, Retention Retention) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        Package created = await context.SendAsync(new CreatePackage { Kind = Kind, Name = Name, Retention = Retention }).ConfigureAwait(false);
        return Print(context, TextTable.Pairs(
        [
            ("Created", $"{Formats.Word(created.Kind)} {created.Name}"),
            ("Package", created.PackageId.Value.ToHex()),
        ]));
    }
}
