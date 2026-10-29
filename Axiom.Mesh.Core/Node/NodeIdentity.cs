namespace Axiom.Mesh.Core;

/// <summary>Who a node is: its device, and the user whose Roster lists it (LOCAL-SPEC.md §6.1).</summary>
public sealed class NodeIdentity
{
    /// <summary>The node's identity.</summary>
    /// <param name="device">Its device id.</param>
    /// <param name="user">Its user id.</param>
    public NodeIdentity(DeviceId device, UserId user)
    {
        Device = device;
        User = user;
    }

    /// <summary>The device id: <c>H</c> of the device key (SPEC.md §2.3).</summary>
    public DeviceId Device { get; }

    /// <summary>The user id: <c>H</c> of the user key.</summary>
    public UserId User { get; }

    /// <inheritdoc />
    public override string ToString() => $"device {Device} of user {User}";
}
