namespace Axiom.Mesh.Cli;

/// <summary>What <see cref="WritePathRule"/> decided: directly as <c>cli</c>, or through a named service. Only the two factories make one.</summary>
internal sealed record WritePath
{
    private WritePath(string? service) => Service = service;

    /// <summary>Write directly, as the external service <c>cli</c>.</summary>
    public static WritePath Direct { get; } = new((string?)null);

    /// <summary>The service to call; <see langword="null"/> for a direct write.</summary>
    public string? Service { get; }

    /// <summary>Whether the write goes through a service.</summary>
    public bool IsViaService => Service is not null;

    /// <summary>Write through a service with <c>ServiceCall</c>.</summary>
    /// <param name="service">The service's name.</param>
    /// <returns>The path.</returns>
    public static WritePath ViaService(string service) => new(service);

    /// <inheritdoc />
    public override string ToString() => Service is null ? "direct" : "via " + Service;
}
