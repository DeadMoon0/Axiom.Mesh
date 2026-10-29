namespace Axiom.Mesh.Local;

/// <summary>The status of a <c>Response</c> or <c>Reply</c> (LOCAL-SPEC.md §11.1, §14.9).</summary>
/// <remarks>A value this enumeration does not name (21 to 32 767 reserved, 32 768 to 65 535 service-defined, §11.2) is kept as its raw value and tolerated (§1.5).</remarks>
public enum Status : ushort
{
    /// <summary>The OK body follows.</summary>
    Ok = 0,

    /// <summary>The message id is in the request or callback range and unknown to the receiver.</summary>
    UnknownRequest = 1,

    /// <summary>The connection's role may not send this request, or the request reaches outside what the role may touch.</summary>
    NotPermitted = 2,

    /// <summary>The named user, device, Package, Entry, object, pin, peer, pairing, operation, subscription, series, address or path does not exist here, or no peer can supply an object.</summary>
    NotFound = 3,

    /// <summary>An identity, static peer, subscription id, session or path already exists.</summary>
    AlreadyExists = 4,

    /// <summary>A value is in bounds but wrong in substance; <c>Error.Detail</c> names the field.</summary>
    Invalid = 5,

    /// <summary>The request needs an identity and the device has none.</summary>
    NoIdentity = 6,

    /// <summary>The request needs the user's private key, which is on another device.</summary>
    UserKeyAbsent = 7,

    /// <summary>The request needs the Package's owner.</summary>
    NotOwner = 8,

    /// <summary>The own user has no <c>Write</c> right on the Package, or this device is removed.</summary>
    NotWritable = 9,

    /// <summary>Another connected service holds the kind.</summary>
    KindTaken = 10,

    /// <summary>The request needs a kind this service did not register.</summary>
    KindNotRegistered = 11,

    /// <summary>The service is not configured or not connected.</summary>
    ServiceUnavailable = 12,

    /// <summary>The service did not reply in time.</summary>
    ServiceTimeout = 13,

    /// <summary>A limit of §12 is reached: outstanding callbacks, subscriptions, operations, static peers, one pairing.</summary>
    Busy = 14,

    /// <summary>The session is Draining.</summary>
    Draining = 15,

    /// <summary>The status of an operation in state Cancelled (§8.2); never the status of a <c>Response</c>.</summary>
    Cancelled = 16,

    /// <summary>The file system refused; <c>Error.Detail</c> carries the platform message.</summary>
    IoError = 17,

    /// <summary>No parents make the next clock well-formed and faultless; the Package is read-only on this device.</summary>
    ClockExhausted = 18,

    /// <summary>The replay would exceed 256 Entries; page with <c>ListStored</c> first.</summary>
    GapTooLarge = 19,

    /// <summary>Storing the object would put the pinned bytes over the budget; nothing evictable can make room.</summary>
    StoreFull = 20,
}
