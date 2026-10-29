namespace Axiom.Mesh;

/// <summary>
/// The newest Entry one device wrote to a Package (SPEC.md §7.2, §9.3): a Package has one Head per writing device.
/// </summary>
/// <param name="Device">The writing device.</param>
/// <param name="Entry">The Entry's hash.</param>
/// <param name="Clock">The Entry's clock.</param>
[ClosedShape("The layout of a Head is fixed by SPEC.md §7.2.")]
public readonly record struct Head(DeviceId Device, ObjectHash Entry, ulong Clock);
