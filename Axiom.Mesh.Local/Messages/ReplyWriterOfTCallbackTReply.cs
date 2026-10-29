using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>The one <see cref="BodyWriter"/> per callback class that writes its reply body.</summary>
/// <typeparam name="TCallback">The callback.</typeparam>
/// <typeparam name="TReply">Its reply body.</typeparam>
internal static class ReplyWriter<TCallback, TReply>
    where TCallback : LocalCallback, ICallback<TCallback, TReply>
{
    /// <summary>Writes a boxed <typeparamref name="TReply"/>.</summary>
    public static readonly BodyWriter Write = Box;

    private static void Box(object body, ref WireWriter writer) => TCallback.WriteReply(ref writer, (TReply)body);
}
