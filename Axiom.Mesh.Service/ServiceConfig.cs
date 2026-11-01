using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Service;

/// <summary>
/// What a service run by hand reads from the Host's <c>config.json</c>: its token (<c>services.&lt;name&gt;.token</c>, an
/// external service, LOCAL-SPEC.md §4.2) and the pipe's name (<c>pipe.name</c>). Read with <see cref="JsonDocument"/>, which
/// needs no reflection and runs ahead of time. A spawned service needs neither: its environment carries both.
/// </summary>
public static class ServiceConfig
{
    /// <summary>A service's token in a configuration text.</summary>
    /// <param name="json">The text of <c>config.json</c>; <see langword="null"/> when there is none.</param>
    /// <param name="serviceName">The service's name.</param>
    /// <param name="token">The token, or <see cref="ServiceToken.None"/>.</param>
    /// <returns><see langword="true"/> when the text names one that parses as 64 hex digits.</returns>
    public static bool TryReadToken(string? json, string serviceName, out ServiceToken token)
    {
        token = ServiceToken.None;
        return TryRead(json, ["services", serviceName, "token"], out string? text) && ServiceToken.TryParseHex(text, out token);
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
