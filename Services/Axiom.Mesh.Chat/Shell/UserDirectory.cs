using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The users and Rosters this Host knows, as one session of the service reads them (LOCAL-SPEC.md §6.2, §6.3:
/// <c>ListKnownUsers</c>, <c>ListDevices</c>): read once, and again when a device no Roster lists turns up; a device still
/// unknown after that is not asked about again in the session. The shell behind <see cref="AuthorResolution"/>.
/// </summary>
internal sealed class UserDirectory
{
    private readonly LocalClient client;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<DeviceId> unknown = [];
    private IReadOnlyList<User> users = [];
    private IReadOnlyList<Device> devices = [];
    private bool loaded;

    /// <summary>A directory over a session's connection.</summary>
    /// <param name="client">The connection.</param>
    public UserDirectory(LocalClient client) => this.client = client;

    /// <summary>The author of a device.</summary>
    /// <param name="device">The device.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The author; by its id alone when no Roster held here lists it.</returns>
    public async Task<AuthorView> AuthorAsync(DeviceId device, CancellationToken cancellationToken)
    {
        await KnowAsync(device, cancellationToken).ConfigureAwait(false);
        return AuthorResolution.Of(device, users, devices);
    }

    /// <summary>A Channel's owner with the devices known, for SPEC.md §9.4.</summary>
    /// <param name="owner">The owner.</param>
    /// <param name="device">A device whose membership is about to be asked, so that it is looked up first.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The owner.</returns>
    public async Task<ChannelOwner> OwnerAsync(UserId owner, DeviceId device, CancellationToken cancellationToken)
    {
        await KnowAsync(device, cancellationToken).ConfigureAwait(false);
        return new ChannelOwner(owner, devices);
    }

    /// <summary>A user's display name.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The name; empty when unknown.</returns>
    public async Task<string> NameOfAsync(UserId user, CancellationToken cancellationToken)
    {
        if (!loaded)
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return users.FirstOrDefault(u => u.UserId == user)?.Name ?? string.Empty;
    }

    /// <inheritdoc />
    public override string ToString() => $"{users.Count} users, {devices.Count} devices known";

    private async Task KnowAsync(DeviceId device, CancellationToken cancellationToken)
    {
        bool asked;
        lock (unknown)
            asked = unknown.Contains(device);
        if (loaded && (asked || devices.Any(d => d.DeviceId == device)))
            return;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (devices.Any(d => d.DeviceId == device))
            return;
        lock (unknown)
            unknown.Add(device);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LocalResult<IReadOnlyList<User>> known = await client.SendAsync(new ListKnownUsers(), cancellationToken).ConfigureAwait(false);
            if (!known.IsOk)
                return;
            var rosters = new List<Device>();
            foreach (User user in known.Value)
            {
                LocalResult<IReadOnlyList<Device>> listed = await client.SendAsync(new ListDevices { UserId = user.UserId }, cancellationToken).ConfigureAwait(false);
                if (listed.IsOk)
                    rosters.AddRange(listed.Value);
            }

            users = known.Value;
            devices = rosters;
            loaded = true;
        }
        finally
        {
            gate.Release();
        }
    }
}
