using System.IO;
using System.Text;

namespace Axiom.Mesh.Testing;

/// <summary>Standard output of an in-process command line: kept whole for the result, and each write handed on at once.</summary>
internal sealed class EchoWriter : TextWriter
{
    private readonly object gate = new();
    private readonly StringBuilder written = new();
    private readonly TextWriter echo;

    /// <summary>A writer that echoes to another.</summary>
    /// <param name="echo">Receives every write.</param>
    public EchoWriter(TextWriter echo) => this.echo = echo;

    /// <inheritdoc />
    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>Everything written so far.</summary>
    public string Written
    {
        get
        {
            lock (gate)
                return written.ToString();
        }
    }

    /// <inheritdoc />
    public override void Write(char value)
    {
        lock (gate)
            written.Append(value);
        echo.Write(value);
    }

    /// <inheritdoc />
    public override void Write(string? value)
    {
        lock (gate)
            written.Append(value);
        echo.Write(value);
    }

    /// <inheritdoc />
    public override void WriteLine(string? value)
    {
        lock (gate)
            written.Append(value).Append(NewLine);
        echo.WriteLine(value);
    }
}
