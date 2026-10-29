namespace Axiom.Mesh.Core;

/// <summary>What a held object waits for, and whose: a user, a Package or a device id.</summary>
/// <param name="Kind">What is missing.</param>
/// <param name="Subject">The user, Package or device it is missing for.</param>
internal readonly record struct HeldNeed(HeldNeedKind Kind, Hash32 Subject);
