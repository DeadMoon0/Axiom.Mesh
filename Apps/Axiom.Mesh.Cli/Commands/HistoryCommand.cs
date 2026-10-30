using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh history &lt;package&gt; [--limit &lt;n&gt;] [--reverse] [--json]</c>: a Package's Entries in the total order
/// every node agrees on (SPEC.md §9.3), oldest first, or newest first with <c>--reverse</c>. A withdrawn Message shows
/// as withdrawn, never its text.
/// </summary>
/// <param name="Package">The Package as typed: id, prefix or name.</param>
/// <param name="Limit">How many Entries at most.</param>
/// <param name="Reverse">Whether to list newest first.</param>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record HistoryCommand(string Package, int Limit, bool Reverse, bool Json) : Command
{
    /// <summary>The default number of Entries listed.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The most Entries one command lists.</summary>
    public const int MaxLimit = 10_000;

    private const int Page = 64;

    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        PackageId package = await IdResolver.PackageAsync(context, Package).ConfigureAwait(false);
        var entries = new List<Entry>();
        EntryKey? after = null;
        while (entries.Count < Limit)
        {
            var request = new ListHistory { PackageId = package, After = after, Reverse = Reverse, Limit = (ushort)Math.Min(Page, Limit - entries.Count) };
            EntryPage page = await context.SendAsync(request).ConfigureAwait(false);
            entries.AddRange(page.Entries);
            if (!page.More || page.Entries.Count == 0)
                break;
            Entry last = page.Entries[^1];
            after = new EntryKey(last.Clock, last.DeviceId, last.Hash);
        }

        return Json ? Print(context, HistoryView.Json(entries)) : Print(context, HistoryView.Text(entries));
    }
}
