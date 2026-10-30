using System.Collections.Generic;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// Turns what the user typed into an id, reading the candidates from the Host with the list requests and choosing with
/// <see cref="IdMatch"/>. A full id is taken as it is; a prefix or a name must name exactly one candidate.
/// </summary>
internal static class IdResolver
{
    private const ushort StoredPage = 64;
    private const int MaxStoredScanned = 65_536;

    /// <summary>A Package: full id, a prefix of a listed or followed one, or its exact name.</summary>
    /// <param name="context">The command's context.</param>
    /// <param name="text">What the user typed.</param>
    /// <returns>The Package's id.</returns>
    public static async Task<PackageId> PackageAsync(CommandContext context, string text)
    {
        if (IdMatch.IsFull(text, out Hash32 full))
            return new PackageId(full);
        var all = new List<Package>();
        PackageId? after = null;
        while (true)
        {
            PackagePage page = await context.SendAsync(new ListPackages { After = after }).ConfigureAwait(false);
            all.AddRange(page.Packages);
            if (!page.More || page.Packages.Count == 0)
                break;
            after = page.Packages[^1].PackageId;
        }

        return Choose(all, p => p.PackageId.Value, p => p.Name, "package", text).PackageId;
    }

    /// <summary>A known user: full id, a prefix, or the exact display name.</summary>
    /// <param name="context">The command's context.</param>
    /// <param name="text">What the user typed.</param>
    /// <returns>The user's id.</returns>
    public static async Task<UserId> UserAsync(CommandContext context, string text)
    {
        if (IdMatch.IsFull(text, out Hash32 full))
            return new UserId(full);
        IReadOnlyList<User> users = await context.SendAsync(new ListKnownUsers()).ConfigureAwait(false);
        return Choose(users, u => u.UserId.Value, u => u.Name, "user", text).UserId;
    }

    /// <summary>A device of a known user: full id, a prefix, or its exact name in its Roster.</summary>
    /// <param name="context">The command's context.</param>
    /// <param name="text">What the user typed.</param>
    /// <returns>The device's id.</returns>
    public static async Task<DeviceId> DeviceAsync(CommandContext context, string text)
    {
        if (IdMatch.IsFull(text, out Hash32 full))
            return new DeviceId(full);
        var devices = new List<Device>();
        foreach (User user in await context.SendAsync(new ListKnownUsers()).ConfigureAwait(false))
            devices.AddRange(await context.SendAsync(new ListDevices { UserId = user.UserId }).ConfigureAwait(false));
        return Choose(devices, d => d.DeviceId.Value, d => d.Name, "device", text).DeviceId;
    }

    /// <summary>A stored Entry: its full hash, or a prefix of the hash of an Entry this Host stores.</summary>
    /// <param name="context">The command's context.</param>
    /// <param name="text">What the user typed.</param>
    /// <returns>The Entry's hash.</returns>
    public static async Task<ObjectHash> EntryAsync(CommandContext context, string text)
    {
        if (IdMatch.IsFull(text, out Hash32 full))
            return new ObjectHash(full);
        var seen = new List<Entry>();
        ulong from = 1;
        while (seen.Count < MaxStoredScanned)
        {
            EntryPage page = await context.SendAsync(new ListStored { FromSeq = from, Limit = StoredPage }).ConfigureAwait(false);
            seen.AddRange(page.Entries);
            if (!page.More || page.Entries.Count == 0)
                break;
            from = page.Entries[^1].StoreSeq + 1;
        }

        return Choose(seen, e => e.Hash.Value, _ => string.Empty, "entry", text).Hash;
    }

    /// <summary>An object: its full hash, or a prefix of a stored Entry's hash.</summary>
    /// <param name="context">The command's context.</param>
    /// <param name="text">What the user typed.</param>
    /// <returns>The object's hash.</returns>
    public static Task<ObjectHash> ObjectAsync(CommandContext context, string text) => EntryAsync(context, text);

    private static T Choose<T>(IReadOnlyList<T> candidates, System.Func<T, Hash32> id, System.Func<T, string> name, string what, string text)
    {
        if (IdMatch.TryFind(text, candidates, id, name, what, out T? match, out string? problem, out bool ambiguous))
            return match!;
        throw ambiguous ? CliFailure.Usage(problem!) : CliFailure.Problem(problem!);
    }
}
