using System;

namespace Axiom.Mesh.Cli;

/// <summary>
/// Progress in bytes on standard error, one line rewritten in place, for work the file helpers of Axiom.Mesh.Local do on
/// this side of the pipe. Reported synchronously, so no line is written after the work ended.
/// </summary>
/// <param name="context">The command's context; its error writer is a terminal.</param>
/// <param name="what">What is being done, for the line.</param>
internal sealed class BytesProgress(CommandContext context, string what) : IProgress<long>
{
    /// <inheritdoc />
    public void Report(long value) => context.Error.Write($"\r{what}: {Formats.Bytes((ulong)Math.Max(0, value))}");
}
