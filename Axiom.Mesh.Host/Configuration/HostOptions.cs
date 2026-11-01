using System.Collections.Generic;

namespace Axiom.Mesh.Host;

/// <summary>
/// <c>config.json</c> (fundamentals plan §2.6): every key optional with a documented default, every unknown key refused
/// at start. Read and written with System.Text.Json's source generator, so the daemon runs trimmed and ahead of time.
/// </summary>
/// <remarks>Settable, not init-only: the source generator sets init-only properties a file leaves out to null, not to their default.</remarks>
internal sealed record HostOptions
{
    /// <summary>The mesh listener and discovery.</summary>
    public MeshSection Mesh { get; set; } = new();

    /// <summary>The store.</summary>
    public StoreSection Store { get; set; } = new();

    /// <summary>The pipe.</summary>
    public PipeSection Pipe { get; set; } = new();

    /// <summary>
    /// The services, by name (LOCAL-SPEC.md §4.2). Default: <c>cli</c>, external, with a token generated on first start,
    /// Package kinds 1 and 2 and Entry kinds 1 to 3; <c>chat</c> and <c>sync</c>, spawned from <c>axiom-mesh-chat</c> and
    /// <c>axiom-mesh-sync</c> beside the daemon when they are there (S-D11).
    /// </summary>
    public Dictionary<string, ServiceSection> Services { get; set; } = new()
    {
        ["cli"] = ServiceSection.DefaultCli,
        ["chat"] = ServiceSection.DefaultChat,
        ["sync"] = ServiceSection.DefaultSync,
    };

    /// <summary>The user-key requests (LOCAL-SPEC.md §2.2).</summary>
    public UserKeyRequestsSection UserKeyRequests { get; set; } = new();
}
