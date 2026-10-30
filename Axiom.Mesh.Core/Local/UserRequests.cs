using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB.Entity;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Local;
using LocalDevice = Axiom.Mesh.Local.Device;

namespace Axiom.Mesh.Core;

/// <summary>
/// The requests of LOCAL-SPEC.md §6.2 and §6.3 about known users and devices: <c>ListKnownUsers</c>, <c>ForgetUser</c>,
/// <c>ListDevices</c>, <c>RemoveDevice</c>. Pairing itself (<c>StartPairing</c> and the rest) is milestone M10's.
/// </summary>
internal static class UserRequests
{
    private const int MaxUsers = 1024;
    private const int MaxDevices = 256;

    /// <summary><c>ListKnownUsers</c> (276): every known user, the own one included, ascending by user id.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> ListKnownUsersAsync(RequestContext context, uint correlation, ListKnownUsers request, CancellationToken cancellationToken)
    {
        TrustSnapshot trust = context.Node.Trust;
        Dictionary<UserId, long> since = await KnownSinceAsync(context.Node.Writer, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<User> users = [.. trust.Known
            .Order()
            .Take(MaxUsers)
            .Select(u => UserOf(trust, u, since.GetValueOrDefault(u)))];
        return LocalAnswer.Ok<ListKnownUsers, IReadOnlyList<User>>(correlation, users);
    }

    /// <summary>
    /// <c>ForgetUser</c> (277): the user's Roster dropped, its sessions closed, the user no longer listed; refused as
    /// <see cref="ForgetRule"/> says.
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> ForgetUserAsync(RequestContext context, uint correlation, ForgetUser request, CancellationToken cancellationToken)
    {
        if (ForgetRule.Check(context.Node.Trust, request.UserId) is { } refusal)
            return refusal.Answer(correlation);

        await context.Node.Writer.SendAsync(new ForgetCommand(request.UserId), cancellationToken).ConfigureAwait(false);
        return LocalAnswer.Ok<ForgetUser, NoContent>(correlation, default);
    }

    /// <summary><c>ListDevices</c> (288): the devices of a known user's current Roster, removed ones included, ascending by id.</summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static ValueTask<LocalAnswer> ListDevicesAsync(RequestContext context, uint correlation, ListDevices request, CancellationToken cancellationToken)
    {
        TrustSnapshot trust = context.Node.Trust;
        UserId user = request.UserId.Value.IsZero ? trust.Self!.User : request.UserId;
        if (!trust.Known.Contains(user))
            return ValueTask.FromResult(LocalAnswer.Failure(correlation, Status.NotFound, $"User {user} is not known here.", "userId"));

        IReadOnlyList<LocalDevice> devices = trust.TryGetUser(user, out UserTrust? held)
            ? [.. LocalRecords.DevicesOf(held, trust.Self?.Device, context.Node.Table.View).Take(MaxDevices)]
            : [];
        return ValueTask.FromResult(LocalAnswer.Ok<ListDevices, IReadOnlyList<LocalDevice>>(correlation, devices));
    }

    /// <summary>
    /// <c>RemoveDevice</c> (289): a Roster with seq one higher marking the device removed, which closes its sessions and is
    /// pushed to every established peer (SPEC.md §7.6, §8.8). Needs the user key; <c>Invalid</c> for this device,
    /// <c>NotFound</c> for a device not the own user's. A device removed already is answered as it stands.
    /// </summary>
    /// <param name="context">The connection and the node.</param>
    /// <param name="correlation">The request's correlation.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the connection ends.</param>
    /// <returns>The answer.</returns>
    public static async ValueTask<LocalAnswer> RemoveDeviceAsync(RequestContext context, uint correlation, RemoveDevice request, CancellationToken cancellationToken)
    {
        MeshNode node = context.Node;
        SelfIdentity self = node.Trust.Self!;
        if (request.DeviceId == self.Device)
            return LocalAnswer.Failure(correlation, Status.Invalid, "This device cannot remove itself; remove it from another device of the user.", "deviceId");
        if (!node.Trust.TryGetUser(self.User, out UserTrust? owned) || !owned.Devices.Any(d => d.Device.Id == request.DeviceId))
            return LocalAnswer.Failure(correlation, Status.NotFound, $"Device {request.DeviceId} is not a device of the own user.", "deviceId");

        (Status status, LocalDevice? device) = await node.WithUserKeyAsync<LocalDevice>(
            async (signer, ct) =>
            {
                if (!node.Trust.TryGetUser(self.User, out UserTrust? own) || own.Roster is not { } current || !current.Content.TryGetDevice(request.DeviceId, out _))
                    return (Status.NotFound, null);
                if (!own.Devices.Any(d => d.Device.Id == request.DeviceId && d.IsRemoved))
                {
                    VerifiedRoster next = new RosterDraft
                    {
                        Seq = current.Content.Seq + 1,
                        Devices = [.. current.Content.Devices.Select(d => d.Id == request.DeviceId ? new RosterDevice(d.Key, d.Name, d.AddedAt, removed: true) : d)],
                    }.Sign(signer);
                    await node.Store.PutAsync(next, ct).ConfigureAwait(false);
                }

                UserTrust user = node.Trust.TryGetUser(self.User, out UserTrust? stored) ? stored : own;
                EffectiveDevice removed = user.Devices.First(d => d.Device.Id == request.DeviceId);
                return (Status.Ok, LocalRecords.DeviceOf(removed, self.User, self.Device, node.Table.Find(request.DeviceId)?.LastSeen));
            },
            cancellationToken).ConfigureAwait(false);
        return status switch
        {
            Status.Ok => LocalAnswer.Ok<RemoveDevice, LocalDevice>(correlation, device!),
            Status.UserKeyAbsent => LocalAnswer.Failure(correlation, status, "Removing a device needs the user key, which is on another device of this user: remove it there.", string.Empty),
            _ => LocalAnswer.Failure(correlation, status, $"Device {request.DeviceId} is not a device of the own user.", "deviceId"),
        };
    }

    private static User UserOf(TrustSnapshot trust, UserId user, long sinceMs)
    {
        string name = trust.Self is { } self && self.User == user ? self.UserName : string.Empty;
        return trust.TryGetUser(user, out UserTrust? held)
            ? LocalRecords.UserOf(held, trust.Self?.User, sinceMs, name)
            : new User { UserId = user, Name = name, Self = trust.Self?.User == user, KnownSince = LocalRecords.Milliseconds(sinceMs) };
    }

    private static async Task<Dictionary<UserId, long>> KnownSinceAsync(StoreWriter writer, CancellationToken cancellationToken)
    {
        await using LdbContext context = await LdbContext.OpenAsync(writer.Database, cancellationToken).ConfigureAwait(false);
        List<UserRow> rows = await context.Set<UserRow>().ReadOnlyQuery().ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(r => new UserId(RowKeys.Hash(r.User)), r => r.SinceMs);
    }
}
