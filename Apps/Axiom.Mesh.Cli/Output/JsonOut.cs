using System;
using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Axiom.Mesh.Cli;

/// <summary>
/// The <c>--json</c> output: written with <see cref="Utf8JsonWriter"/> field by field, so it needs no reflection and runs
/// ahead of time. Ids are full lower-case hex, times milliseconds since the Unix epoch, enumerations lower-case words.
/// </summary>
internal static class JsonOut
{
    private static JsonWriterOptions Options => new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>A JSON document as text.</summary>
    /// <param name="write">Writes the one value of the document.</param>
    /// <returns>The text, indented, without a final line break.</returns>
    public static string Of(Action<Utf8JsonWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Options))
            write(writer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan).ReplaceLineEndings("\n");
    }

    /// <summary>Writes an id as full hex.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="name">The property.</param>
    /// <param name="id">The id.</param>
    public static void Id(Utf8JsonWriter writer, string name, Hash32 id) => writer.WriteString(name, id.ToHex());
}
