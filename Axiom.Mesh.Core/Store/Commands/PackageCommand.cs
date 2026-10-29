namespace Axiom.Mesh.Core;

/// <summary>Changes a Package's state or policy; completes with whether anything changed.</summary>
internal sealed class PackageCommand : StoreCommand<bool>
{
    /// <summary>A change of a Package.</summary>
    /// <param name="kind">What to do.</param>
    /// <param name="package">The Package.</param>
    /// <param name="policy">For <see cref="PackageCommandKind.SetPolicy"/>, the policy.</param>
    public PackageCommand(PackageCommandKind kind, PackageId package, LocalRetention policy)
    {
        Kind = kind;
        Package = package;
        Policy = policy;
    }

    /// <summary>What to do.</summary>
    public PackageCommandKind Kind { get; }

    /// <summary>The Package.</summary>
    public PackageId Package { get; }

    /// <summary>The policy, for <see cref="PackageCommandKind.SetPolicy"/>.</summary>
    public LocalRetention Policy { get; }
}
