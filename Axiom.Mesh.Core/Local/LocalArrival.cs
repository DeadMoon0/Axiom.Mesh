namespace Axiom.Mesh.Core;

/// <summary>What the local port knows of a connection between adopting it and serving it: who it is and its bytes.</summary>
/// <param name="Identity">The process identity the Host read, or the in-process service.</param>
/// <param name="Activity">Its bytes below the protocol: silence and frame completion.</param>
internal sealed record LocalArrival(ProcessIdentity Identity, LinkActivity Activity);
