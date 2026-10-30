using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh entry &lt;hash&gt; [--json]</c>: one stored Entry in full (LOCAL-SPEC.md §6.4).</summary>
/// <param name="Hash">The Entry's hash or a prefix of it.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record EntryCommand(string Hash, bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        ObjectHash hash = await IdResolver.EntryAsync(context, Hash).ConfigureAwait(false);
        Entry entry = await context.SendAsync(new GetEntry { Hash = hash }).ConfigureAwait(false);
        return Json ? Print(context, HistoryView.ShowJson(entry)) : Print(context, HistoryView.ShowText(entry));
    }
}
