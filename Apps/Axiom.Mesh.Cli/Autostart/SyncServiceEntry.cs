using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axiom.Mesh.Cli;

/// <summary>
/// The <c>services.sync</c> entry <c>host install</c> adds to a <c>config.json</c> that an older Host wrote without it (S-D11): the
/// daemon then spawns <c>axiom-mesh-sync</c> beside it as the default configuration of a new install does. A configuration without
/// a <c>services</c> object, or with a <c>sync</c> entry already (whatever it says), is left alone. Pure.
/// </summary>
internal static class SyncServiceEntry
{
    private const string Entry = """{"spawn":"axiom-mesh-sync","kinds":[1],"entryKinds":[1]}""";

    /// <summary>The configuration with <c>services.sync</c> added.</summary>
    /// <param name="config">The text of <c>config.json</c>; <see langword="null"/> when there is none (the Host writes the defaults, sync among them).</param>
    /// <returns>The new text, or <see langword="null"/> when nothing needs adding or the text is no JSON object.</returns>
    public static string? Added(string? config)
    {
        if (string.IsNullOrWhiteSpace(config))
            return null;
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(config, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is not JsonObject document || document["services"] is not JsonObject services || services.ContainsKey("sync"))
            return null;
        services["sync"] = JsonNode.Parse(Entry);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            document.WriteTo(writer);
        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }
}
