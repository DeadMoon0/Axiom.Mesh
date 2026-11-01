namespace Axiom.Mesh.Chat;

/// <summary>
/// Whether this device may withdraw a message (CHAT-SERVICE.md §5.7, SPEC.md §9.4): it must be indexed and not withdrawn
/// already, and this device must be the one that wrote it or a device of the Channel's owner. The Host would store a
/// Redaction that SPEC.md §9.4 does not honour without a word, so the service checks first. Pure.
/// </summary>
internal static class WithdrawRule
{
    /// <summary>Decides.</summary>
    /// <param name="indexed">The message is indexed.</param>
    /// <param name="withdrawn">It is withdrawn already.</param>
    /// <param name="author">The device that wrote it.</param>
    /// <param name="owner">The Channel's owner.</param>
    /// <param name="ownDevice">This device.</param>
    /// <param name="ownUser">This device's user.</param>
    /// <returns>Pass, or why not.</returns>
    public static Verdict Check(bool indexed, bool withdrawn, DeviceId author, UserId owner, DeviceId ownDevice, UserId ownUser)
    {
        if (!indexed)
            return Verdict.Refuse(ChatStatus.NotAMessage, "No such message is indexed.", "message");
        if (withdrawn)
            return Verdict.Refuse(ChatStatus.Withdrawn, "The message is withdrawn already.", "message");
        if (author != ownDevice && owner != ownUser)
            return Verdict.Refuse(ChatStatus.NotYours, "Only the device that wrote the message, or a device of the Channel's owner, may withdraw it.", "message");
        return Verdict.Pass;
    }
}
