namespace Axiom.Mesh.Core;

/// <summary>
/// Treats the own user's current Grant of a Package as withdrawn (SPEC.md §7.2): a device of the owner listed the Package
/// before and now omits it. Completes with whether a Grant was withdrawn now; a newer Grant counts again.
/// </summary>
internal sealed class WithdrawGrantCommand : StoreCommand<bool>
{
    /// <summary>A withdrawal.</summary>
    /// <param name="package">The Package the owner's listing omits.</param>
    /// <param name="origin">The owner's device whose listing omits it.</param>
    public WithdrawGrantCommand(PackageId package, Origin origin)
    {
        Package = package;
        Origin = origin;
    }

    /// <summary>The Package.</summary>
    public PackageId Package { get; }

    /// <summary>The owner's device whose listing omits it.</summary>
    public Origin Origin { get; }
}
