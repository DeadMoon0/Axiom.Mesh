namespace Axiom.Mesh.Core;

/// <summary>How the store keeps objects: the device class whose retention row applies by default, and the budget.</summary>
internal sealed record StoreOptions
{
    /// <summary>Desktop, without a budget.</summary>
    public static StoreOptions Default { get; } = new();

    /// <summary>The device class (SPEC.md §11.2).</summary>
    public DeviceClass DeviceClass { get; init; } = DeviceClass.Desktop;

    /// <summary>The bytes eviction keeps the store under, and the most pinned bytes a local write may raise it to (LOCAL-SPEC.md §6.5).</summary>
    public long BudgetBytes { get; init; } = long.MaxValue;
}
