namespace Axiom.Mesh.Core;

/// <summary>What the duplicate rule (SPEC.md §4.5) decided when a session became established.</summary>
/// <param name="KeepThis">Whether the new session is kept; when not, it is closed with <c>Bye(Duplicate)</c>.</param>
/// <param name="CloseOther">The older session to close with <c>Bye(Duplicate)</c>, when the new one replaced it.</param>
internal readonly record struct EstablishVerdict(bool KeepThis, PeerSession? CloseOther);
