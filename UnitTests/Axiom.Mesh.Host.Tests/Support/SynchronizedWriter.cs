using System.IO;

namespace Axiom.Mesh.Host.Tests.Support;

/// <summary>A writer the daemon writes to from its threads while a test reads what it said so far.</summary>
internal sealed class SynchronizedWriter : StringWriter
{
    private readonly object gate = new();

    /// <summary>Everything written so far.</summary>
    public string Text
    {
        get
        {
            lock (gate)
                return ToString();
        }
    }

    /// <inheritdoc />
    public override void Write(char value)
    {
        lock (gate)
            base.Write(value);
    }

    /// <inheritdoc />
    public override void Write(string? value)
    {
        lock (gate)
            base.Write(value);
    }

    /// <inheritdoc />
    public override void WriteLine(string? value)
    {
        lock (gate)
            base.WriteLine(value);
    }
}
