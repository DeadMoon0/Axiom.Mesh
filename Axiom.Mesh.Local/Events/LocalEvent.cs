using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// The body of one event of LOCAL-SPEC.md §7.3: what the Host pushes on a subscription. Every event is a sealed record
/// that also implements <see cref="IEvent{TSelf}"/>, which names its message id and its topic. The Host sends one
/// inside an <see cref="EventFrame"/>; a client receives it through <c>LocalClient.SubscribeAsync</c>.
/// </summary>
public abstract record LocalEvent
{
    private protected LocalEvent()
    {
    }

    /// <summary>The event's message id (§14.1), the same as its static <c>Id</c>.</summary>
    public abstract ushort MessageId { get; }

    /// <summary>Writes the event's body, after the subscription of its frame.</summary>
    /// <param name="writer">The writer.</param>
    /// <exception cref="System.ArgumentException">A field is outside its bounds.</exception>
    public abstract void Write(ref WireWriter writer);
}
