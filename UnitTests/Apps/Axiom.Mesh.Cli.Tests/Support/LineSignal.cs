using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Axiom.Mesh.Cli.Tests.Support;

/// <summary>Standard output of a command that runs until cancelled: signals once a text was written.</summary>
internal sealed class LineSignal : TextWriter
{
    private readonly object gate = new();
    private readonly StringBuilder written = new();
    private readonly string expected;
    private readonly TaskCompletionSource seen = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>A writer waiting for a text.</summary>
    /// <param name="expected">The text.</param>
    public LineSignal(string expected) => this.expected = expected;

    /// <inheritdoc />
    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>Completes once the text was written; fails the test after <see cref="TestMesh.Deadline"/>.</summary>
    public Task Seen => seen.Task.WaitAsync(TestMesh.Deadline);

    /// <inheritdoc />
    public override void Write(char value)
    {
        lock (gate)
        {
            written.Append(value);
            if (written.ToString().Contains(expected, StringComparison.Ordinal))
                seen.TrySetResult();
        }
    }
}
