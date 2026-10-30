using System.Text.Json;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// What the command line reads from the Host's <c>config.json</c>: the token of the external service <c>cli</c>
/// (<c>services.cli.token</c>, decision D4), which the write commands present. Read with <see cref="JsonDocument"/>,
/// which needs no reflection and runs ahead of time.
/// </summary>
internal static class CliConfig
{
    /// <summary>The token of service <c>cli</c> in a configuration text.</summary>
    /// <param name="json">The text of <c>config.json</c>; <see langword="null"/> when there is none.</param>
    /// <param name="token">The token, or <see cref="ServiceToken.None"/>.</param>
    /// <returns><see langword="true"/> when the text names one that parses as 64 hex digits.</returns>
    public static bool TryReadCliToken(string? json, out ServiceToken token)
    {
        token = ServiceToken.None;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("services", out JsonElement services) && services.ValueKind == JsonValueKind.Object
                && services.TryGetProperty("cli", out JsonElement cli) && cli.ValueKind == JsonValueKind.Object
                && cli.TryGetProperty("token", out JsonElement value) && value.ValueKind == JsonValueKind.String
                && ServiceToken.TryParseHex(value.GetString(), out token);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
