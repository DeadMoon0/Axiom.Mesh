using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh pins [--json]</c>: every pin and why it holds its object (LOCAL-SPEC.md §6.5).</summary>
/// <param name="Json">Whether to print JSON.</param>
internal sealed record PinsCommand(bool Json) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        var pins = new List<Pin>();
        PinKey? after = null;
        while (true)
        {
            PinPage page = await context.SendAsync(new ListPins { After = after }).ConfigureAwait(false);
            pins.AddRange(page.Pins);
            if (!page.More || page.Pins.Count == 0)
                break;
            Pin last = page.Pins[^1];
            after = new PinKey { Hash = last.Hash, Reason = last.Reason, Source = last.Source };
        }

        return Json ? Print(context, StoreView.PinsJson(pins)) : Print(context, StoreView.PinsText(pins));
    }
}
