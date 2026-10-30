using System.Net;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh dial &lt;device|ip:port&gt;</c>: asks the Host to dial a known device at the address discovery knows,
/// or an address once (LOCAL-SPEC.md §6.6). The dial runs in the background; <c>peers</c> shows how it went.
/// </summary>
/// <param name="Target">A device id or name, or an <c>ip:port</c>.</param>
internal sealed record DialCommand(string Target) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        Dial dial = IsAddress(Target)
            ? new Dial { Address = Target }
            : new Dial { DeviceId = await IdResolver.DeviceAsync(context, Target).ConfigureAwait(false) };
        await context.SendAsync(dial).ConfigureAwait(false);
        return Print(context, $"dialling {Target}; 'axiom-mesh peers' shows how it went");
    }

    private static bool IsAddress(string text) => text.Contains(':', System.StringComparison.Ordinal) && IPEndPoint.TryParse(text, out _);
}
