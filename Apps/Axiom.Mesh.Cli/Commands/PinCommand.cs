using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary><c>axiom-mesh pin &lt;hash&gt;</c>: the user keeps a stored object whatever the policies say (LOCAL-SPEC.md §6.5).</summary>
/// <param name="Hash">The object's hash, or a prefix of a stored Entry's.</param>
internal sealed record PinCommand(string Hash) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        ObjectHash hash = await IdResolver.ObjectAsync(context, Hash).ConfigureAwait(false);
        Pin pin = await context.SendAsync(new PinObject { Hash = hash }).ConfigureAwait(false);
        return Print(context, $"pinned {pin.Hash.Value.ToHex()} ({Formats.Bytes(pin.Size)})");
    }
}
