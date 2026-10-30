using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Discovery;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Host;

/// <summary>
/// Reads, checks and completes <c>config.json</c> (fundamentals plan §2.6): a missing file is the defaults, written out
/// on first start; an unknown key or a value out of range stops the start with the steps out; an external service
/// without a token gets one, 32 bytes from the daemon's randomness, written back to the file, which only the user can
/// read (LOCAL-SPEC.md §4.2, §13).
/// </summary>
internal static class HostConfiguration
{
    private const int MaxServiceNameBytes = 32;

    /// <summary>Reads the configuration, writing the defaults or new tokens back when there are any.</summary>
    /// <param name="file">The configuration file.</param>
    /// <param name="entropy">The randomness tokens are made of.</param>
    /// <param name="cancellationToken">Cancels reading and writing.</param>
    /// <returns>The configuration, complete and checked.</returns>
    /// <exception cref="MeshConfigurationException">The file is not valid JSON, has a key the daemon does not know, or a value out of range.</exception>
    public static async Task<HostOptions> LoadAsync(string file, IEntropy entropy, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        ArgumentNullException.ThrowIfNull(entropy);
        HostOptions options = File.Exists(file)
            ? Parse(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false), file)
            : new HostOptions();
        if (Problems(options) is { Count: > 0 } problems)
        {
            throw new MeshConfigurationException(
                $"{file} cannot be used: {string.Join("; ", problems)}.",
                "Correct the value, or remove the key to use its default; every key and its default is listed in the README of Axiom.Mesh.Host.");
        }

        (HostOptions completed, bool changed) = WithTokens(options, entropy);
        if (changed || !File.Exists(file))
            await WriteAsync(file, completed, cancellationToken).ConfigureAwait(false);
        return completed;
    }

    /// <summary>Reads a configuration from its text; keys the daemon does not know are refused.</summary>
    /// <param name="json">The file's text.</param>
    /// <param name="file">The file's path, for the message.</param>
    /// <returns>The configuration, not yet checked.</returns>
    /// <exception cref="MeshConfigurationException">The text is not valid JSON or has a key the daemon does not know.</exception>
    public static HostOptions Parse(string json, string file)
    {
        try
        {
            return JsonSerializer.Deserialize(json, HostOptionsJson.Default.HostOptions)
                ?? throw new JsonException("The file holds null instead of an object.");
        }
        catch (JsonException ex)
        {
            throw new MeshConfigurationException(
                $"{file} cannot be read at {ex.Path ?? "$"}: {ex.Message}",
                "Correct or remove the key named there; keys are camelCase and every key and its default is listed in the README of Axiom.Mesh.Host.",
                $"Or move {file} aside: the daemon writes the defaults on its next start (with a new cli token).");
        }
    }

    /// <summary>The configuration as the file holds it, indented.</summary>
    /// <param name="options">The configuration.</param>
    /// <returns>The text.</returns>
    public static string Write(HostOptions options) => JsonSerializer.Serialize(options, HostOptionsJson.Default.HostOptions);

    /// <summary>What is wrong with a configuration, one entry per problem.</summary>
    /// <param name="options">The configuration.</param>
    /// <returns>The problems; empty when it is usable.</returns>
    public static IReadOnlyList<string> Problems(HostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        if (options.Mesh is null || options.Store is null || options.Pipe is null || options.Services is null || options.UserKeyRequests is null)
        {
            problems.Add("a section is null; remove it to use its defaults");
            return problems;
        }

        if (options.Mesh.Port is < 0 or > 65535)
            problems.Add($"mesh.port is {options.Mesh.Port}, not 0 to 65535");
        if (options.Mesh.Listen is { } listen && !IPAddress.TryParse(listen, out _))
            problems.Add($"mesh.listen \"{listen}\" is not an IP address");
        foreach (string peer in options.Mesh.StaticPeers ?? [])
        {
            if (!TryParseEndPoint(peer, out _))
                problems.Add($"mesh.staticPeers has \"{peer}\", which is not host:port");
        }

        if (options.Store.BudgetBytes < 0)
            problems.Add($"store.budgetBytes is {options.Store.BudgetBytes}, not 0 or more");
        if (options.Pipe.Name is { Length: 0 })
            problems.Add("pipe.name is empty; remove it for the platform's default");
        foreach ((string name, ServiceSection? service) in options.Services)
            problems.AddRange(ServiceProblems(name, service));
        foreach (string path in options.UserKeyRequests.ConfineTo ?? [])
        {
            if (!Path.IsPathFullyQualified(path))
                problems.Add($"userKeyRequests.confineTo has \"{path}\", which is not an absolute path");
        }

        return problems;
    }

    /// <summary>Gives every external service without a token a new one (LOCAL-SPEC.md §4.2).</summary>
    /// <param name="options">The configuration.</param>
    /// <param name="entropy">The randomness.</param>
    /// <returns>The configuration, and whether a token was made.</returns>
    public static (HostOptions Options, bool Changed) WithTokens(HostOptions options, IEntropy entropy)
    {
        bool changed = false;
        Span<byte> token = stackalloc byte[Limits.ServiceTokenLength];
        var services = new Dictionary<string, ServiceSection>(StringComparer.Ordinal);
        foreach ((string name, ServiceSection service) in options.Services)
        {
            if (service.External && service.Token.Length == 0)
            {
                entropy.Fill(token);
                services[name] = service with { Token = Convert.ToHexString(token).ToLowerInvariant() };
                changed = true;
            }
            else
            {
                services[name] = service;
            }
        }

        return (options with { Services = services }, changed);
    }

    /// <summary>What the node is told: the mesh port and address, the budget, the local port's services and confinement.</summary>
    /// <param name="options">The checked configuration.</param>
    /// <param name="hostUser">The operating-system user the daemon runs as.</param>
    /// <param name="version">The daemon's version, for <c>HelloAck</c>.</param>
    /// <returns>The node's options.</returns>
    public static MeshNodeOptions NodeOptionsOf(HostOptions options, string hostUser, string version) => MeshNodeOptions.Default with
    {
        MeshPort = options.Mesh.Port,
        ListenAddress = options.Mesh.Listen is { } listen ? IPAddress.Parse(listen) : null,
        StoreBudgetBytes = options.Store.BudgetBytes,
        StaticPeers = [.. options.Mesh.StaticPeers],
        Local = new LocalPortOptions
        {
            Services = [.. options.Services.Select(s => ServiceOf(s.Key, s.Value)).OrderBy(s => s.Name, StringComparer.Ordinal)],
            ConfineUserKeyRequestsTo = [.. options.UserKeyRequests.ConfineTo],
            HostUser = hostUser,
            HostVersion = "axiom-meshd/" + version,
        },
    };

    /// <summary>Reads <c>host:port</c> as an IP endpoint or a DNS endpoint.</summary>
    /// <param name="text">The text.</param>
    /// <param name="endPoint">The endpoint.</param>
    /// <returns><see langword="true"/> when it is one.</returns>
    public static bool TryParseEndPoint(string text, out EndPoint endPoint)
    {
        bool parsed = StaticPeers.TryParseAddress(text, out EndPoint? address);
        endPoint = address!;
        return parsed;
    }

    private static LocalService ServiceOf(string name, ServiceSection service) => new()
    {
        Name = name,
        Provenance = service.InProcess ? ServiceProvenance.InProcess : service.Spawn is not null ? ServiceProvenance.Spawned : ServiceProvenance.External,
        Token = service.External && ServiceToken.TryParseHex(service.Token, out ServiceToken token) ? token : ServiceToken.None,
        Kinds = [.. service.Kinds.Order().Select(k => (PackageKind)k)],
        EntryKinds = [.. service.EntryKinds.Order().Select(k => (EntryKind)k)],
    };

    private static IEnumerable<string> ServiceProblems(string name, ServiceSection? service)
    {
        if (service is null)
        {
            yield return $"services.{name} is null";
            yield break;
        }

        int bytes = Encoding.UTF8.GetByteCount(name);
        if (bytes is 0 or > MaxServiceNameBytes || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            yield return $"services has the name \"{name}\"; a service name is 1 to 32 ASCII letters, digits, '.', '-' and '_' (LOCAL-SPEC.md §3.2)";
        int provenances = (service.InProcess ? 1 : 0) + (service.Spawn is not null ? 1 : 0) + (service.External ? 1 : 0);
        if (provenances != 1)
            yield return $"services.{name} needs exactly one of inProcess, spawn and external";
        if (service.Spawn is { } spawn && !Path.IsPathFullyQualified(spawn))
            yield return $"services.{name}.spawn \"{spawn}\" is not an absolute path";
        if (service.Token is null || (service.Token.Length != 0 && !ServiceToken.TryParseHex(service.Token, out _)))
            yield return $"services.{name}.token is not 64 hex characters; remove it and the daemon generates a new one";
        if (!service.External && service.Token is { Length: > 0 })
            yield return $"services.{name}.token is for an external service only";
        if (service.Kinds is null || service.EntryKinds is null)
            yield return $"services.{name}.kinds and entryKinds are lists";
        else if (service.Kinds.Count > 16 || service.EntryKinds.Count > 64 || service.Kinds.Distinct().Count() != service.Kinds.Count || service.EntryKinds.Distinct().Count() != service.EntryKinds.Count)
            yield return $"services.{name} lists at most 16 distinct kinds and 64 distinct entryKinds (LOCAL-SPEC.md §5.12)";
    }

    // The file and its directory are the user's alone: the tokens in it open the service role (LOCAL-SPEC.md §13).
    private static async Task WriteAsync(string file, HostOptions options, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(file))!;
        HostFiles.CreatePrivateDirectory(directory);
        string temporary = file + ".tmp";
        await HostFiles.WritePrivateAsync(temporary, Encoding.UTF8.GetBytes(Write(options) + Environment.NewLine), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, file, overwrite: true);
    }
}
