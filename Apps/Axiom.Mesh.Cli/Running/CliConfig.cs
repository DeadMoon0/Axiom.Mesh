using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// What the command line reads from the Host's <c>config.json</c>: the token of the external service <c>cli</c>
/// (<c>services.cli.token</c>, decision D4), which the write commands present, and the pipe's name (<c>pipe.name</c>),
/// so the command line finds a Host configured with a pipe of its own. Read with <see cref="JsonDocument"/>, which needs
/// no reflection and runs ahead of time.
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
        return TryRead(json, ["services", "cli", "token"], out string? text) && ServiceToken.TryParseHex(text, out token);
    }

    /// <summary>The pipe's name in a configuration text (<c>pipe.name</c>), which the Host listens on unless <c>AXIOM_MESH_PIPE</c> is set.</summary>
    /// <param name="json">The text of <c>config.json</c>; <see langword="null"/> when there is none.</param>
    /// <param name="name">The name; <see langword="null"/> when the text names none.</param>
    /// <returns><see langword="true"/> when the text names a pipe that is not empty.</returns>
    public static bool TryReadPipeName(string? json, [NotNullWhen(true)] out string? name)
    {
        name = TryRead(json, ["pipe", "name"], out string? text) && text is { Length: > 0 } ? text : null;
        return name is not null;
    }

    // A string at a path of objects; anything else (no text, not JSON, another shape) is "not there".
    private static bool TryRead(string? json, string[] path, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            JsonElement element = document.RootElement;
            foreach (string name in path)
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
                    return false;
            }

            if (element.ValueKind != JsonValueKind.String)
                return false;
            value = element.GetString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
