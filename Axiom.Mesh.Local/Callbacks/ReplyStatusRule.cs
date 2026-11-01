namespace Axiom.Mesh.Local;

/// <summary>
/// Which statuses a service may answer a callback with (LOCAL-SPEC.md §9.8, §11.1, §11.2): <c>Ok</c>; the codes of §11.1
/// with their meaning there, except <c>Cancelled</c>, which is never the status of an answer; and the service-defined codes
/// from 32 768. A reserved code (21 to 32 767) is a violation of the service's connection, and the Host closes it.
/// </summary>
public static class ReplyStatusRule
{
    /// <summary>The first service-defined status (§11.2).</summary>
    public const ushort FirstServiceDefined = 32_768;

    /// <summary>Whether a service may send a <c>Reply</c> with this status.</summary>
    /// <param name="status">The status.</param>
    /// <returns><see langword="true"/> for <c>Ok</c>, 1 to 15, 17 to 20 and 32 768 to 65 535.</returns>
    public static bool MayReply(Status status)
    {
        ushort code = (ushort)status;
        return code >= FirstServiceDefined || (code <= (ushort)Status.StoreFull && status != Status.Cancelled);
    }

    /// <summary>Whether a status is service-defined: its meaning is in the service's own document (§11.2).</summary>
    /// <param name="status">The status.</param>
    /// <returns><see langword="true"/> from 32 768.</returns>
    public static bool IsServiceDefined(Status status) => (ushort)status >= FirstServiceDefined;
}
