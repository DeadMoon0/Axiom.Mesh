using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Core;

/// <summary>
/// The dispatcher's table (fundamentals plan §2.5): every request of <see cref="RequestTable"/> by message id, with the
/// checks LOCAL-SPEC.md states for it and the handler this Host serves it with. Data, not a switch: a later milestone
/// adds a handler with <see cref="With"/> or a row in <see cref="Handlers"/> and the dispatch loop does not change.
/// </summary>
internal sealed class RequestRoutes
{
    // §6, header: the requests that fail with NoIdentity before an identity exists. StartPairing depends on its mode
    // (ShowDevice needs none), so its handler decides.
    private static readonly FrozenSet<ushort> NeedingIdentity = new ushort[]
    {
        258, 277, 288, 289, 304, 305, 306, 307, 308, 309, 310, 311, 312, 313, 314, 315, 316, 317, 318, 319, 353, 354, 357, 374, 385, 513,
    }.ToFrozenSet();

    // §2.2: the requests that spend the user's private key.
    private static readonly FrozenSet<ushort> SpendingUserKey = new ushort[] { 273, 277, 289, 312, 313 }.ToFrozenSet();

    // §3.4: what a Draining session still serves.
    private static readonly FrozenSet<ushort> WhileDraining = new ushort[] { 401, 416, 417, 418 }.ToFrozenSet();

    // §3.4: requests that write but touch no Package, so nothing orders them against the connection's other requests: a
    // PutObject stores an object named by its hash, idempotently, and many run at once (STRUCTURAL-DEBT.md 57).
    private static readonly FrozenSet<ushort> Unordered = new ushort[] { PutObject.Id }.ToFrozenSet();

    private readonly FrozenDictionary<ushort, RequestRoute> routes;

    private RequestRoutes(IEnumerable<RequestRoute> rows) => routes = rows.ToFrozenDictionary(r => r.Descriptor.Id);

    /// <summary>The table of this Host: the phase-1 request set (fundamentals plan §2.5); everything else answers <c>UnknownRequest</c> (D7).</summary>
    public static RequestRoutes Default { get; } = Build(Handlers());

    /// <summary>Every row, ascending by id.</summary>
    public IEnumerable<RequestRoute> All => routes.Values.OrderBy(r => r.Descriptor.Id);

    /// <summary>The handlers this Host serves, by request id.</summary>
    /// <returns>The map.</returns>
    public static IReadOnlyDictionary<ushort, RequestHandler> Handlers() => new Dictionary<ushort, RequestHandler>
    {
        [GetIdentity.Id] = Of<GetIdentity>(IdentityRequests.GetIdentityAsync),
        [CreateIdentity.Id] = Of<CreateIdentity>(IdentityRequests.CreateIdentityAsync),
        [RenameDevice.Id] = Of<RenameDevice>(IdentityRequests.RenameDeviceAsync),
        [GetStore.Id] = Of<GetStore>(NodeRequests.GetStoreAsync),
        [ListServices.Id] = Of<ListServices>(NodeRequests.ListServicesAsync),
        [ListPeers.Id] = Of<ListPeers>(NodeRequests.ListPeersAsync),
        [ListSessions.Id] = Of<ListSessions>(NodeRequests.ListSessionsAsync),
        [ListDiscovered.Id] = Of<ListDiscovered>(NodeRequests.ListDiscoveredAsync),
        [Subscribe.Id] = Of<Subscribe>(SubscriptionRequests.SubscribeAsync),
        [Unsubscribe.Id] = Of<Unsubscribe>(SubscriptionRequests.UnsubscribeAsync),
        [ListOperations.Id] = Of<ListOperations>(OperationRequests.ListOperationsAsync),
        [GetOperation.Id] = Of<GetOperation>(OperationRequests.GetOperationAsync),
        [CancelOperation.Id] = Of<CancelOperation>(OperationRequests.CancelOperationAsync),
        [StartPairing.Id] = Of<StartPairing>(PairingRequests.StartPairingAsync),
        [ConfirmPairing.Id] = Of<ConfirmPairing>(PairingRequests.ConfirmPairingAsync),
        [CancelPairing.Id] = Of<CancelPairing>(PairingRequests.CancelPairingAsync),
        [GetPairing.Id] = Of<GetPairing>(PairingRequests.GetPairingAsync),
        [ListKnownUsers.Id] = Of<ListKnownUsers>(UserRequests.ListKnownUsersAsync),
        [ForgetUser.Id] = Of<ForgetUser>(UserRequests.ForgetUserAsync),
        [ListDevices.Id] = Of<ListDevices>(UserRequests.ListDevicesAsync),
        [RemoveDevice.Id] = Of<RemoveDevice>(UserRequests.RemoveDeviceAsync),
        [ListPackages.Id] = Of<ListPackages>(PackageRequests.ListPackagesAsync),
        [GetPackage.Id] = Of<GetPackage>(PackageRequests.GetPackageAsync),
        [CreatePackage.Id] = Of<CreatePackage>(PackageRequests.CreatePackageAsync),
        [FollowPackage.Id] = Of<FollowPackage>(PackageRequests.FollowPackageAsync),
        [UnfollowPackage.Id] = Of<UnfollowPackage>(PackageRequests.UnfollowPackageAsync),
        [SetPackagePolicy.Id] = Of<SetPackagePolicy>(PackageRequests.SetPackagePolicyAsync),
        [ListGrants.Id] = Of<ListGrants>(GrantRequests.ListGrantsAsync),
        [SetGrant.Id] = Of<SetGrant>(GrantRequests.SetGrantAsync),
        [RevokeGrant.Id] = Of<RevokeGrant>(GrantRequests.RevokeGrantAsync),
        [ListHistory.Id] = Of<ListHistory>(HistoryRequests.ListHistoryAsync),
        [GetEntry.Id] = Of<GetEntry>(HistoryRequests.GetEntryAsync),
        [GetObject.Id] = Of<GetObject>(HistoryRequests.GetObjectAsync),
        [ListStored.Id] = Of<ListStored>(HistoryRequests.ListStoredAsync),
        [ListPins.Id] = Of<ListPins>(StoreRequests.ListPinsAsync),
        [PinObject.Id] = Of<PinObject>(StoreRequests.PinObjectAsync),
        [Unpin.Id] = Of<Unpin>(StoreRequests.UnpinAsync),
        [ReleasePins.Id] = Of<ReleasePins>(StoreRequests.ReleasePinsAsync),
        [SetStoreBudget.Id] = Of<SetStoreBudget>(StoreRequests.SetStoreBudgetAsync),
        [ListStaticPeers.Id] = Of<ListStaticPeers>(PeerRequests.ListStaticPeersAsync),
        [AddStaticPeer.Id] = Of<AddStaticPeer>(PeerRequests.AddStaticPeerAsync),
        [RemoveStaticPeer.Id] = Of<RemoveStaticPeer>(PeerRequests.RemoveStaticPeerAsync),
        [Dial.Id] = Of<Dial>(PeerRequests.DialAsync),
        [Fetch.Id] = Of<Fetch>(OperationStarts.FetchAsync),
        [RegisterKind.Id] = Of<RegisterKind>(ServiceRequests.RegisterKindAsync),
        [WriteEntry.Id] = Of<WriteEntry>(ServiceRequests.WriteEntryAsync),
        [PutObject.Id] = Of<PutObject>(ServiceRequests.PutObjectAsync),
        [GetStorePath.Id] = Of<GetStorePath>(ServiceRequests.GetStorePathAsync),
        [ListSeries.Id] = Of<ListSeries>(TelemetryRequests.AbsentAsync),
        [ListKeys.Id] = Of<ListKeys>(TelemetryRequests.AbsentAsync),
        [GetTotal.Id] = Of<GetTotal>(TelemetryRequests.AbsentAsync),
        [QueryTelemetry.Id] = Of<QueryTelemetry>(TelemetryRequests.AbsentAsync),
    };

    /// <summary>The table for a set of handlers: every request of the protocol, served or not.</summary>
    /// <param name="handlers">The handlers by request id.</param>
    /// <returns>The table.</returns>
    public static RequestRoutes Build(IReadOnlyDictionary<ushort, RequestHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        return new RequestRoutes(RequestTable.Default.All.Select(d => new RequestRoute(
            d,
            handlers.GetValueOrDefault(d.Id),
            NeedingIdentity.Contains(d.Id),
            SpendingUserKey.Contains(d.Id),
            WhileDraining.Contains(d.Id),
            Ordered: !d.Name.StartsWith("Get", StringComparison.Ordinal) && !d.Name.StartsWith("List", StringComparison.Ordinal) && !Unordered.Contains(d.Id))));
    }

    /// <summary>A typed handler as a row's handler.</summary>
    /// <typeparam name="TRequest">The request it serves.</typeparam>
    /// <param name="handler">The typed handler.</param>
    /// <returns>The handler.</returns>
    public static RequestHandler Of<TRequest>(Func<RequestContext, uint, TRequest, CancellationToken, ValueTask<LocalAnswer>> handler)
        where TRequest : LocalRequest =>
        (context, correlation, request, cancellationToken) => handler(context, correlation, (TRequest)request, cancellationToken);

    /// <summary>This table with one more or one replaced handler.</summary>
    /// <param name="id">The request id.</param>
    /// <param name="handler">Its handler.</param>
    /// <returns>The new table.</returns>
    public RequestRoutes With(ushort id, RequestHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!routes.ContainsKey(id))
            throw new ArgumentException($"Request id {id} is not a request of LOCAL-SPEC.md §14.1.", nameof(id));
        return new RequestRoutes(routes.Values.Select(r => r.Descriptor.Id == id ? r with { Handler = handler } : r));
    }

    /// <summary>The row of a request id.</summary>
    /// <param name="id">The id.</param>
    /// <param name="route">The row.</param>
    /// <returns><see langword="true"/> when the protocol knows the id.</returns>
    public bool TryGet(ushort id, out RequestRoute route) => routes.TryGetValue(id, out route!);
}
