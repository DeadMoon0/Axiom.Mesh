using System;
using System.IO;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh object &lt;hash&gt; [--out &lt;file&gt;]</c>: one stored object (LOCAL-SPEC.md §6.4): its tag and size, or
/// its bytes written to a file with <c>--out</c>.
/// </summary>
/// <param name="Hash">The object's hash, or a prefix of a stored Entry's.</param>
/// <param name="Out">The file to write the bytes to; <see langword="null"/> to describe the object.</param>
internal sealed record ObjectCommand(string Hash, string? Out) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        ObjectHash hash = await IdResolver.ObjectAsync(context, Hash).ConfigureAwait(false);
        ReadOnlyMemory<byte> bytes = await context.SendAsync(new GetObject { Hash = hash }).ConfigureAwait(false);
        if (Out is not null)
        {
            string path = Path.GetFullPath(Path.Combine(context.Environment.CurrentDirectory, Out));
            try
            {
                await context.Environment.WriteFile(path, bytes, context.Cancellation).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw CliFailure.Problem($"{path} cannot be written: {ex.Message}");
            }

            return Print(context, $"{Formats.Bytes((ulong)bytes.Length)} written to {path}");
        }

        string tag = Objects.TryParse(HashedObject.Of(bytes), out ParsedObject? parsed, out _) ? Formats.Word(parsed.Tag) : "malformed";
        return Print(context, TextTable.Pairs(
        [
            ("Object", hash.Value.ToHex()),
            ("Tag", tag),
            ("Size", Formats.Bytes((ulong)bytes.Length)),
        ]));
    }
}
