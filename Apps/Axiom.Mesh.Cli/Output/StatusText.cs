using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// One line for every status of LOCAL-SPEC.md §11.1: what it means for the user and, where one exists, what to do. The
/// Host's own message follows after a colon when it adds something.
/// </summary>
internal static class StatusText
{
    /// <summary>The line for a status.</summary>
    /// <param name="status">The status.</param>
    /// <param name="error">The Host's error, if any.</param>
    /// <param name="device">This device's name, as the Host's identity says it; empty when unknown.</param>
    /// <returns>One line, never empty.</returns>
    public static string Explain(Status status, Error? error, string device)
    {
        string meaning = Meaning(status, device);
        string message = (error?.Message ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return message.Length == 0 ? meaning : $"{meaning}: {message}";
    }

    /// <summary>What a status means, without the Host's message.</summary>
    /// <param name="status">The status.</param>
    /// <param name="device">This device's name; empty when unknown.</param>
    /// <returns>One line.</returns>
    public static string Meaning(Status status, string device) => status switch
    {
        Status.Ok => "done",
        Status.UnknownRequest => "the Host does not serve this request; update axiom-meshd to this tool's version",
        Status.NotPermitted => "the Host does not permit this from here",
        Status.NotFound => "not found",
        Status.AlreadyExists => "it exists already",
        Status.Invalid => "the Host refused a value",
        Status.NoIdentity => "this device has no identity yet; run 'axiom-mesh identity create' or enrol it with 'axiom-mesh pair show --device'",
        Status.UserKeyAbsent => $"the user key is on another device of this user, not on {(device.Length == 0 ? "this device" : device)}; run this there",
        Status.NotOwner => "only the Package's owner may do this",
        Status.NotWritable => "this user may not write to the Package; ask its owner for 'grant set --write'",
        Status.KindTaken => "another connected service holds this kind",
        Status.KindNotRegistered => "the service did not register this kind",
        Status.ServiceUnavailable => "the service is not configured or not connected",
        Status.ServiceTimeout => "the service did not answer in time",
        Status.Busy => "the Host is busy with as much of this as it may; finish or cancel something first",
        Status.Draining => "the Host is shutting down; run this again once it is back",
        Status.Cancelled => "cancelled",
        Status.IoError => "the file system refused",
        Status.ClockExhausted => "the Package's clock is exhausted on this device; it is read-only here",
        Status.GapTooLarge => "too much happened since; list the history first",
        Status.StoreFull => "the store is full: pinned objects fill the budget; unpin something or raise the budget",
        _ => $"the Host refused with status {(ushort)status}",
    };
}
