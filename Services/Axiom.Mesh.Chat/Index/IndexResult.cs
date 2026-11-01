namespace Axiom.Mesh.Chat;

/// <summary>What <see cref="ChatIndex.IndexAsync"/> did with one Entry.</summary>
/// <param name="Change">What changed.</param>
/// <param name="Message">For <see cref="IndexChange.NewMessage"/> the message's row; for <see cref="IndexChange.Withdrew"/> the withdrawn message's.</param>
/// <param name="By">For <see cref="IndexChange.Withdrew"/>, the device whose Redaction withdrew it.</param>
internal sealed record IndexResult(IndexChange Change, MessageRow? Message, DeviceId By)
{
    /// <summary>Nothing changed.</summary>
    public static IndexResult Nothing { get; } = new(IndexChange.Nothing, null, default);
}
