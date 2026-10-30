using System.Text.Json.Serialization;

namespace Axiom.Mesh.Host;

/// <summary>
/// The source-generated JSON contract of <c>config.json</c>: camelCase keys, every unknown key refused, written indented.
/// No reflection, so the daemon publishes with Native AOT.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(HostOptions))]
internal sealed partial class HostOptionsJson : JsonSerializerContext;
