using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>The identity requests of LOCAL-SPEC.md §6.1: <c>GetIdentity</c>, <c>CreateIdentity</c>, <c>RenameDevice</c>.</summary>
internal static class IdentityRequests
{
    /// <summary><c>GetIdentity</c> (256): the identity as it stands; not present before one exists.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> GetIdentityAsync(RequestContext context, uint correlation, GetIdentity request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(LocalAnswer.Ok<GetIdentity, Identity>(correlation, LocalRecords.IdentityOf(context.Node.Trust, context.Node.UserKeyHeld)));

    /// <summary>
    /// <c>CreateIdentity</c> (257): the user key, a Roster with seq 1 holding this device, and the device key unless one is
    /// held already; <c>AlreadyExists</c> when an identity is present.
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> CreateIdentityAsync(RequestContext context, uint correlation, CreateIdentity request, CancellationToken cancellationToken)
    {
        if (request.UserName.Length == 0)
            return LocalAnswer.Failure(correlation, Status.Invalid, "The user name must not be empty.", "userName");
        if (request.DeviceName.Length == 0)
            return LocalAnswer.Failure(correlation, Status.Invalid, "The device name must not be empty.", "deviceName");

        Identity? created = await context.Node.CreateIdentityAsync(request.UserName, request.DeviceName, cancellationToken).ConfigureAwait(false);
        return created is null
            ? LocalAnswer.Failure(correlation, Status.AlreadyExists, "This device has an identity already; a device that is to join an existing user enrols instead.", string.Empty)
            : LocalAnswer.Ok<CreateIdentity, Identity>(correlation, created);
    }

    /// <summary>
    /// <c>RenameDevice</c> (258): a new Roster with seq one higher, the device renamed, pushed to every established peer
    /// (SPEC.md §7.6). Needs the user key.
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> RenameDeviceAsync(RequestContext context, uint correlation, RenameDevice request, CancellationToken cancellationToken)
    {
        if (request.Name.Length == 0)
            return LocalAnswer.Failure(correlation, Status.Invalid, "The device name must not be empty.", "name");

        (Status status, Device? device) = await context.Node.RenameDeviceAsync(request.DeviceId, request.Name, cancellationToken).ConfigureAwait(false);
        return status switch
        {
            Status.Ok => LocalAnswer.Ok<RenameDevice, Device>(correlation, device!),
            Status.UserKeyAbsent => LocalAnswer.Failure(correlation, status, "Renaming a device needs the user key, which is on another device of this user: rename it there.", string.Empty),
            Status.NotFound => LocalAnswer.Failure(correlation, status, $"Device {request.DeviceId} is not a device of the own user.", "deviceId"),
            _ => LocalAnswer.Failure(correlation, status, $"Device {request.DeviceId} is removed; a removed device cannot be renamed.", "deviceId"),
        };
    }
}
