namespace Axiom.Mesh.Core;

/// <summary>The node's own user and device, once an identity exists (LOCAL-SPEC.md §6.1).</summary>
/// <param name="UserKey">The own user's public key; the private half never leaves the key store.</param>
/// <param name="Device">The own device id.</param>
internal sealed record SelfIdentity(PublicKey UserKey, DeviceId Device)
{
    /// <summary>The own user id.</summary>
    public UserId User => UserKey.AsUserId();
}
