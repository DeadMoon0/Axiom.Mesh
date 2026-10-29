using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>What <see cref="LocalHelloRule"/> decided: admit (with the configured service, for the service role) or refuse with a <c>Bye</c>.</summary>
internal sealed class LocalHelloVerdict
{
    private LocalHelloVerdict(LocalService? service, Bye? refusal)
    {
        Service = service;
        Refusal = refusal;
    }

    /// <summary>The service whose name was granted; <see langword="null"/> for the user role or a refusal.</summary>
    public LocalService? Service { get; }

    /// <summary>The <c>Bye</c> to send and close with; <see langword="null"/> when admitted.</summary>
    public Bye? Refusal { get; }

    /// <summary>Whether the Hello is admitted.</summary>
    public bool IsAdmitted => Refusal is null;

    /// <summary>Admits the connection.</summary>
    /// <param name="service">The configured service, for the service role.</param>
    /// <returns>The verdict.</returns>
    public static LocalHelloVerdict Admit(LocalService? service) => new(service, null);

    /// <summary>Refuses the connection.</summary>
    /// <param name="bye">The Bye to send.</param>
    /// <returns>The verdict.</returns>
    public static LocalHelloVerdict Refuse(Bye bye) => new(null, bye);

    /// <inheritdoc />
    public override string ToString() => Refusal is null ? $"admit {Service?.Name ?? "user"}" : $"refuse {Refusal}";
}
